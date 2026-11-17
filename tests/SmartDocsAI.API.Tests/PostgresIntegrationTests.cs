using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.Models;
using SmartDocsAI.API.Services;

namespace SmartDocsAI.API.Tests;

/// <summary>
/// Gerçek PostgreSQL testi. Yerelde SMARTDOCS_TEST_POSTGRES yoksa test başarılı
/// sayılmaz, gerekçesiyle "atlandı" raporlanır. CI'da (CI=true) ise atlanamaz:
/// değişken eksikse test başarısız olur, böylece yapılandırma hatası sessizce
/// testleri devre dışı bırakamaz.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string Variable = "SMARTDOCS_TEST_POSTGRES";

    public PostgresFactAttribute()
    {
        var configured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable));
        var runningInCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
        if (!configured && !runningInCi)
        {
            Skip = $"{Variable} tanımlı değil; PostgreSQL entegrasyon testi atlandı.";
        }
    }
}

public sealed class PostgresIntegrationTests : IAsyncLifetime
{
    private static readonly TimeSpan LeaseTimeout = TimeSpan.FromMinutes(5);
    private string? _databaseConnectionString;
    private string? _databaseName;

    public async Task InitializeAsync()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(PostgresFactAttribute.Variable);
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                $"CI ortamında {PostgresFactAttribute.Variable} tanımlanmalıdır; PostgreSQL testleri atlanamaz.");
        }

        // Her test sınıfı kendi geçici veritabanını kullanır; testler paralel
        // koşsa da birbirinin verisini görmez.
        _databaseName = $"smartdocs_test_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(baseConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        _databaseConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = _databaseName,
            Pooling = false
        }.ConnectionString;

        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_databaseName is null)
        {
            return;
        }

        await using var admin = new NpgsqlConnection(
            Environment.GetEnvironmentVariable(PostgresFactAttribute.Variable));
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task Migrations_ApplyFromScratch_AndMatchTheModel()
    {
        await using var context = NewContext();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        var columns = await context.Database
            .SqlQueryRaw<string>(
                "SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_name = 'Documents'")
            .ToListAsync();
        Assert.Contains("ProcessingLeaseId", columns);
        Assert.Contains("ProcessingHeartbeatAt", columns);
    }

    [PostgresFact]
    public async Task ConcurrentWorkers_CannotClaimTheSameDocumentTwice()
    {
        var documentId = await AddPendingDocumentAsync();
        var now = DateTime.UtcNow;
        using var start = new ManualResetEventSlim(false);

        var workers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await using var context = NewContext();
            await context.Database.OpenConnectionAsync();
            start.Wait();
            return await DocumentProcessingLeases.TryClaimAsync(
                context,
                documentId,
                now,
                LeaseTimeout,
                CancellationToken.None);
        })).ToList();
        await Task.Delay(200);
        start.Set();
        var leases = await Task.WhenAll(workers);

        Assert.Single(leases, lease => lease is not null);
        await using var reader = NewContext();
        var stored = await reader.Documents.AsNoTracking().SingleAsync(item => item.Id == documentId);
        Assert.Equal(leases.Single(lease => lease is not null), stored.ProcessingLeaseId);
    }

    [PostgresFact]
    public async Task StaleWorker_WriteIsRejectedByTheFence()
    {
        var documentId = await AddPendingDocumentAsync();
        await using var staleWorker = NewContext();
        var firstLease = await DocumentProcessingLeases.TryClaimAsync(
            staleWorker, documentId, DateTime.UtcNow, LeaseTimeout, CancellationToken.None);
        var document = await staleWorker.Documents.SingleAsync(item => item.Id == documentId);
        Assert.Equal(firstLease, document.ProcessingLeaseId);

        // İkinci worker, ilkinin heartbeat'i süre aşımına uğramış gibi belgeyi devralır.
        await using var newWorker = NewContext();
        var secondLease = await DocumentProcessingLeases.TryClaimAsync(
            newWorker, documentId, DateTime.UtcNow + LeaseTimeout + TimeSpan.FromSeconds(1), LeaseTimeout, CancellationToken.None);
        Assert.NotNull(secondLease);

        document.IndexingStatus = "Ready";
        document.CurrentIndexVersion = "stale-worker-version";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleWorker.SaveChangesAsync());

        await using var reader = NewContext();
        var stored = await reader.Documents.AsNoTracking().SingleAsync(item => item.Id == documentId);
        Assert.Equal(secondLease, stored.ProcessingLeaseId);
        Assert.Null(stored.CurrentIndexVersion);
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_databaseConnectionString).Options);

    private async Task<int> AddPendingDocumentAsync()
    {
        await using var context = NewContext();
        var document = new Document
        {
            User = new User
            {
                FullName = "PostgreSQL Test",
                Email = $"{Guid.NewGuid():N}@example.com",
                PasswordHash = "hash",
                RoleId = 2
            },
            Title = "Test",
            FileName = "test.pdf",
            FileType = ".pdf",
            FilePath = "test.pdf",
            FileSize = 100,
            UploadDate = DateTime.UtcNow,
            IndexingStatus = "Pending"
        };
        context.Documents.Add(document);
        await context.SaveChangesAsync();
        return document.Id;
    }
}
