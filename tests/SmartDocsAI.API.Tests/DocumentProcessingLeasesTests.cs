using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.Models;
using SmartDocsAI.API.Services;

namespace SmartDocsAI.API.Tests;

public sealed class DocumentProcessingLeasesTests : IAsyncLifetime
{
    private static readonly TimeSpan LeaseTimeout = TimeSpan.FromMinutes(5);

    private SqliteConnection _connection = null!;
    private AppDbContext _context = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options);
        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task TryClaimAsync_GivesPendingDocumentToExactlyOneWorker()
    {
        var document = await AddDocumentAsync("Pending");
        var now = DateTime.UtcNow;

        var first = await DocumentProcessingLeases.TryClaimAsync(_context, document.Id, now, LeaseTimeout, default);
        var second = await DocumentProcessingLeases.TryClaimAsync(_context, document.Id, now, LeaseTimeout, default);

        var stored = await LoadAsync(document.Id);
        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(first, stored.ProcessingLeaseId);
        Assert.Equal("Extracting", stored.IndexingStatus);
        Assert.NotNull(stored.ProcessingHeartbeatAt);
    }

    [Fact]
    public async Task TryClaimAsync_TakesOverOnlyWhenHeartbeatIsStale()
    {
        var now = DateTime.UtcNow;
        var oldLease = Guid.NewGuid();
        var alive = await AddDocumentAsync("Indexing", lease: oldLease, startedAt: now.AddHours(-1), heartbeatAt: now.AddMinutes(-1));
        var crashed = await AddDocumentAsync("Indexing", lease: oldLease, startedAt: now.AddHours(-1), heartbeatAt: now.AddMinutes(-6));

        var aliveClaim = await DocumentProcessingLeases.TryClaimAsync(_context, alive.Id, now, LeaseTimeout, default);
        var crashedClaim = await DocumentProcessingLeases.TryClaimAsync(_context, crashed.Id, now, LeaseTimeout, default);

        Assert.Null(aliveClaim);
        Assert.NotNull(crashedClaim);
        Assert.NotEqual(oldLease, crashedClaim);
        Assert.Equal(crashedClaim, (await LoadAsync(crashed.Id)).ProcessingLeaseId);
    }

    [Fact]
    public async Task TryClaimAsync_UsesStartTimeForRowsWithoutHeartbeat()
    {
        var now = DateTime.UtcNow;
        var recent = await AddDocumentAsync("Extracting", startedAt: now.AddMinutes(-1));
        var abandoned = await AddDocumentAsync("Extracting", startedAt: now.AddMinutes(-10));
        var unknown = await AddDocumentAsync("Extracting");

        Assert.Null(await DocumentProcessingLeases.TryClaimAsync(_context, recent.Id, now, LeaseTimeout, default));
        Assert.NotNull(await DocumentProcessingLeases.TryClaimAsync(_context, abandoned.Id, now, LeaseTimeout, default));
        Assert.NotNull(await DocumentProcessingLeases.TryClaimAsync(_context, unknown.Id, now, LeaseTimeout, default));
    }

    [Fact]
    public async Task FindClaimableAsync_RespectsRetrySchedule_AndUploadOrder()
    {
        var now = DateTime.UtcNow;
        var later = await AddDocumentAsync("Pending", uploadedAt: now.AddMinutes(-1));
        var due = await AddDocumentAsync("RetryWaiting", uploadedAt: now.AddMinutes(-5), nextAttemptAt: now.AddSeconds(-1));
        await AddDocumentAsync("RetryWaiting", uploadedAt: now.AddMinutes(-9), nextAttemptAt: now.AddMinutes(1));
        await AddDocumentAsync("Ready", uploadedAt: now.AddMinutes(-20));

        var claimable = await DocumentProcessingLeases.FindClaimableAsync(_context, now, LeaseTimeout, 10, default);

        Assert.Equal([due.Id, later.Id], claimable);
    }

    [Fact]
    public async Task ReleaseAsync_OnlyReleasesTheCurrentOwnersLease()
    {
        var document = await AddDocumentAsync("Pending");
        var lease = await DocumentProcessingLeases.TryClaimAsync(_context, document.Id, DateTime.UtcNow, LeaseTimeout, default);

        var byStranger = await DocumentProcessingLeases.ReleaseAsync(_context, document.Id, Guid.NewGuid(), default);
        var byOwner = await DocumentProcessingLeases.ReleaseAsync(_context, document.Id, lease!.Value, default);

        var stored = await LoadAsync(document.Id);
        Assert.Equal(0, byStranger);
        Assert.Equal(1, byOwner);
        Assert.Equal("Pending", stored.IndexingStatus);
        Assert.Null(stored.ProcessingLeaseId);
    }

    private async Task<Document> LoadAsync(int id) =>
        await _context.Documents.AsNoTracking().SingleAsync(item => item.Id == id);

    private async Task<Document> AddDocumentAsync(
        string status,
        Guid? lease = null,
        DateTime? startedAt = null,
        DateTime? heartbeatAt = null,
        DateTime? uploadedAt = null,
        DateTime? nextAttemptAt = null)
    {
        var document = new Document
        {
            User = new User
            {
                FullName = "Test User",
                Email = $"{Guid.NewGuid():N}@example.com",
                PasswordHash = "hash",
                RoleId = 2
            },
            Title = "Test",
            FileName = "test.pdf",
            FileType = ".pdf",
            FilePath = "test.pdf",
            FileSize = 100,
            UploadDate = uploadedAt ?? DateTime.UtcNow,
            IndexingStatus = status,
            ProcessingLeaseId = lease,
            ProcessingStartedAt = startedAt,
            ProcessingHeartbeatAt = heartbeatAt,
            NextProcessingAttemptAt = nextAttemptAt
        };
        _context.Documents.Add(document);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return document;
    }
}
