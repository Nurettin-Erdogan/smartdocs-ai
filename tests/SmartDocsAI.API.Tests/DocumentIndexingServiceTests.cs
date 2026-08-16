using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Models;
using SmartDocsAI.API.Services;

namespace SmartDocsAI.API.Tests;

public sealed class DocumentIndexingServiceTests : IAsyncLifetime
{
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
    public async Task ProcessAsync_ExtractsAndIndexesPendingDocument()
    {
        var document = await AddDocumentAsync();
        var qdrant = new FakeQdrantService();
        var service = CreateService(
            new FakeDocumentProcessor(
                new Chunk { DocumentId = document.Id, ChunkIndex = 0, Content = "metin", PageNumber = 1 }),
            qdrant);

        await service.ProcessAsync(document.Id);

        _context.ChangeTracker.Clear();
        var stored = await _context.Documents.Include(item => item.Chunks)
            .SingleAsync(item => item.Id == document.Id);
        Assert.Equal("Ready", stored.IndexingStatus);
        Assert.Null(stored.ProcessingStartedAt);
        Assert.NotNull(stored.CurrentIndexVersion);
        Assert.Single(stored.Chunks);
        Assert.Equal(1, qdrant.SaveCalls);
    }

    [Fact]
    public async Task ProcessAsync_MarksDocumentWithoutTextAsNoContent()
    {
        var document = await AddDocumentAsync();
        var service = CreateService(new FakeDocumentProcessor(), new FakeQdrantService());

        await service.ProcessAsync(document.Id);

        _context.ChangeTracker.Clear();
        var stored = await _context.Documents.SingleAsync(item => item.Id == document.Id);
        Assert.Equal("NoContent", stored.IndexingStatus);
        Assert.Contains("metin", stored.IndexingError!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(stored.ProcessingStartedAt);
    }

    [Fact]
    public async Task ProcessAsync_PersistsFailureForAutomaticRetry()
    {
        var document = await AddDocumentAsync();
        var qdrant = new FakeQdrantService { Exception = new HttpRequestException("Qdrant kapalı") };
        var service = CreateService(
            new FakeDocumentProcessor(
                new Chunk { DocumentId = document.Id, ChunkIndex = 0, Content = "metin", PageNumber = 1 }),
            qdrant);

        await service.ProcessAsync(document.Id);

        _context.ChangeTracker.Clear();
        var stored = await _context.Documents.SingleAsync(item => item.Id == document.Id);
        Assert.Equal("Failed", stored.IndexingStatus);
        Assert.Contains("Qdrant", stored.IndexingError!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(stored.ProcessingStartedAt);
    }

    [Fact]
    public async Task ProcessAsync_SchedulesTransientFailureBeforeFinalAttempt()
    {
        var document = await AddDocumentAsync();
        var qdrant = new FakeQdrantService { Exception = new HttpRequestException("Geçici hata") };
        var service = CreateService(
            new FakeDocumentProcessor(
                new Chunk { DocumentId = document.Id, ChunkIndex = 0, Content = "metin", PageNumber = 1 }),
            qdrant,
            maxAttempts: 3);

        await service.ProcessAsync(document.Id);

        _context.ChangeTracker.Clear();
        var stored = await _context.Documents.SingleAsync(item => item.Id == document.Id);
        Assert.Equal("RetryWaiting", stored.IndexingStatus);
        Assert.Equal(1, stored.ProcessingAttemptCount);
        Assert.NotNull(stored.NextProcessingAttemptAt);
    }

    [Fact]
    public async Task ProcessAsync_ActivatesNewVersionBeforeDeletingOldVectors()
    {
        var document = await AddDocumentAsync();
        document.CurrentIndexVersion = "old-version";
        document.Chunks.Add(new Chunk
        {
            DocumentId = document.Id,
            ChunkIndex = 0,
            Content = "mevcut metin",
            PageNumber = 1
        });
        await _context.SaveChangesAsync();

        var newVersionWasActiveDuringCleanup = false;
        var qdrant = new FakeQdrantService
        {
            DeleteExceptVersionHandler = async (_, newVersion, cancellationToken) =>
            {
                await using var verificationContext = new AppDbContext(
                    new DbContextOptionsBuilder<AppDbContext>()
                        .UseSqlite(_connection)
                        .Options);
                var stored = await verificationContext.Documents
                    .AsNoTracking()
                    .SingleAsync(item => item.Id == document.Id, cancellationToken);
                newVersionWasActiveDuringCleanup =
                    stored.IndexingStatus == "Ready" &&
                    stored.CurrentIndexVersion == newVersion;
            }
        };
        var service = CreateService(new FakeDocumentProcessor(), qdrant);

        await service.ProcessAsync(document.Id);

        Assert.True(newVersionWasActiveDuringCleanup);
    }

    private DocumentIndexingService CreateService(
        IDocumentProcessor processor,
        IQdrantService qdrant,
        int maxAttempts = 1)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DocumentIndexingSettings:MaxAttempts"] = maxAttempts.ToString(),
                ["DocumentIndexingSettings:RetryDelaySeconds"] = "5"
            })
            .Build();

        return new(
            _context,
            processor,
            new FakeOllamaService(),
            qdrant,
            configuration,
            NullLogger<DocumentIndexingService>.Instance);
    }

    private async Task<Document> AddDocumentAsync()
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
            FilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf"),
            FileSize = 100,
            UploadDate = DateTime.UtcNow,
            IndexingStatus = "Extracting",
            ProcessingStartedAt = DateTime.UtcNow
        };
        _context.Documents.Add(document);
        await _context.SaveChangesAsync();
        return document;
    }

    private sealed class FakeDocumentProcessor(params Chunk[] chunks) : IDocumentProcessor
    {
        public Task<List<Chunk>> ProcessPdfAsync(
            Document document,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(chunks.ToList());
    }

    private sealed class FakeOllamaService : IOllamaService
    {
        public Task WarmupAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<float[]> GetEmbeddingAsync(
            string text,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new[] { 0.1f, 0.2f, 0.3f });

        public Task<string> GenerateAnswerAsync(
            string prompt,
            CancellationToken cancellationToken = default) =>
            Task.FromResult("yanıt");

        public async IAsyncEnumerable<string> StreamAnswerAsync(
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return "yanıt";
        }
    }

    private sealed class FakeQdrantService : IQdrantService
    {
        public Exception? Exception { get; set; }
        public int SaveCalls { get; private set; }
        public Func<int, string, CancellationToken, Task>? DeleteExceptVersionHandler { get; init; }

        public Task CreateCollectionIfNotExistsAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveChunksAsync(
            List<Chunk> chunks,
            List<float[]> embeddings,
            string indexVersion,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Exception is null ? Task.CompletedTask : Task.FromException(Exception);
        }

        public Task DeleteDocumentChunksAsync(
            int documentId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteDocumentChunksExceptVersionAsync(
            int documentId,
            string indexVersion,
            CancellationToken cancellationToken = default) =>
            DeleteExceptVersionHandler?.Invoke(documentId, indexVersion, cancellationToken)
            ?? Task.CompletedTask;

        public Task<List<QdrantSearchResult>> SearchSimilarChunksAsync(
            float[] queryVector,
            int limit,
            IReadOnlyDictionary<int, string?> documentVersions,
            double minimumScore,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<QdrantSearchResult>());
    }
}
