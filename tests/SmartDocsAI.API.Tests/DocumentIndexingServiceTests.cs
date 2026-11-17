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
    private static readonly Guid Lease = Guid.NewGuid();
    private static readonly Guid StolenLease = Guid.NewGuid();

    private SqliteConnection _connection = null!;
    private AppDbContext _context = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _context = NewContext();
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
        var service = CreateService(new FakeDocumentProcessor(Chunks(document.Id, 1)), qdrant);

        await service.ProcessAsync(document.Id, Lease);

        var stored = await LoadAsync(document.Id);
        Assert.Equal("Ready", stored.IndexingStatus);
        Assert.Null(stored.ProcessingStartedAt);
        Assert.Null(stored.ProcessingLeaseId);
        Assert.Null(stored.ProcessingHeartbeatAt);
        Assert.NotNull(stored.CurrentIndexVersion);
        Assert.Single(stored.Chunks);
        Assert.Equal(1, qdrant.SaveCalls);
    }

    [Fact]
    public async Task ProcessAsync_MarksDocumentWithoutTextAsNoContent()
    {
        var document = await AddDocumentAsync();
        var service = CreateService(new FakeDocumentProcessor(), new FakeQdrantService());

        await service.ProcessAsync(document.Id, Lease);

        var stored = await LoadAsync(document.Id);
        Assert.Equal("NoContent", stored.IndexingStatus);
        Assert.Contains("metin", stored.IndexingError!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(stored.ProcessingStartedAt);
        Assert.Null(stored.ProcessingLeaseId);
    }

    [Fact]
    public async Task ProcessAsync_PersistsFailureForAutomaticRetry()
    {
        var document = await AddDocumentAsync();
        var qdrant = new FakeQdrantService { Exception = new HttpRequestException("Qdrant kapalı") };
        var service = CreateService(new FakeDocumentProcessor(Chunks(document.Id, 1)), qdrant);

        await service.ProcessAsync(document.Id, Lease);

        var stored = await LoadAsync(document.Id);
        Assert.Equal("Failed", stored.IndexingStatus);
        Assert.Contains("Qdrant", stored.IndexingError!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(stored.ProcessingStartedAt);
        Assert.Null(stored.ProcessingLeaseId);
    }

    [Fact]
    public async Task ProcessAsync_SchedulesTransientFailureBeforeFinalAttempt()
    {
        var document = await AddDocumentAsync();
        var qdrant = new FakeQdrantService { Exception = new HttpRequestException("Geçici hata") };
        var service = CreateService(
            new FakeDocumentProcessor(Chunks(document.Id, 1)),
            qdrant,
            maxAttempts: 3);

        await service.ProcessAsync(document.Id, Lease);

        var stored = await LoadAsync(document.Id);
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
                await using var verificationContext = NewContext();
                var stored = await verificationContext.Documents
                    .AsNoTracking()
                    .SingleAsync(item => item.Id == document.Id, cancellationToken);
                newVersionWasActiveDuringCleanup =
                    stored.IndexingStatus == "Ready" &&
                    stored.CurrentIndexVersion == newVersion;
            }
        };
        var service = CreateService(new FakeDocumentProcessor(), qdrant);

        await service.ProcessAsync(document.Id, Lease);

        Assert.True(newVersionWasActiveDuringCleanup);
    }

    [Fact]
    public async Task ProcessAsync_IgnoresDocumentOwnedByAnotherLease()
    {
        var document = await AddDocumentAsync();
        var processor = new FakeDocumentProcessor(Chunks(document.Id, 1));
        var service = CreateService(processor, new FakeQdrantService());

        await service.ProcessAsync(document.Id, Guid.NewGuid());

        var stored = await LoadAsync(document.Id);
        Assert.Equal(0, processor.Calls);
        Assert.Equal("Extracting", stored.IndexingStatus);
        Assert.Equal(Lease, stored.ProcessingLeaseId);
    }

    [Fact]
    public async Task ProcessAsync_DoesNotInsertChunksWhenLeaseIsStolenDuringExtraction()
    {
        var document = await AddDocumentAsync();
        var processor = new FakeDocumentProcessor(Chunks(document.Id, 3))
        {
            OnProcess = () => StealLeaseAsync(document.Id)
        };
        var qdrant = new FakeQdrantService();
        var service = CreateService(processor, qdrant);

        await service.ProcessAsync(document.Id, Lease);

        var stored = await LoadAsync(document.Id);
        Assert.Empty(stored.Chunks);
        Assert.Equal(StolenLease, stored.ProcessingLeaseId);
        Assert.Equal(0, qdrant.SaveCalls);
    }

    [Fact]
    public async Task ProcessAsync_StopsAtNextHeartbeatWhenLeaseIsStolenDuringEmbedding()
    {
        var document = await AddDocumentAsync();
        var ollama = new FakeOllamaService { OnEmbedding = call => call == 1 ? StealLeaseAsync(document.Id) : Task.CompletedTask };
        var qdrant = new FakeQdrantService();
        var service = CreateService(
            new FakeDocumentProcessor(Chunks(document.Id, 9)),
            qdrant,
            ollama: ollama);

        await service.ProcessAsync(document.Id, Lease);

        var stored = await LoadAsync(document.Id);
        Assert.Equal(StolenLease, stored.ProcessingLeaseId);
        Assert.NotEqual("Ready", stored.IndexingStatus);
        Assert.Null(stored.CurrentIndexVersion);
        Assert.Equal(0, qdrant.SaveCalls);
        Assert.True(ollama.Calls < 9, "Embedding üretimi sahiplik kaybedildikten sonra durmalı.");
    }

    [Fact]
    public async Task ProcessAsync_DoesNotActivateIndexWhenLeaseIsStolenDuringVectorWrite()
    {
        var document = await AddDocumentAsync();
        var qdrant = new FakeQdrantService { SaveHandler = () => StealLeaseAsync(document.Id) };
        var service = CreateService(new FakeDocumentProcessor(Chunks(document.Id, 1)), qdrant);

        await service.ProcessAsync(document.Id, Lease);

        var stored = await LoadAsync(document.Id);
        Assert.Equal(StolenLease, stored.ProcessingLeaseId);
        Assert.Null(stored.CurrentIndexVersion);
        Assert.Equal("Extracting", stored.IndexingStatus);
    }

    [Fact]
    public async Task ProcessAsync_FailureAfterLostLeaseDoesNotOverwriteTheNewOwner()
    {
        var document = await AddDocumentAsync();
        var qdrant = new FakeQdrantService
        {
            SaveHandler = async () =>
            {
                await StealLeaseAsync(document.Id);
                throw new HttpRequestException("Qdrant kapalı");
            }
        };
        var service = CreateService(new FakeDocumentProcessor(Chunks(document.Id, 1)), qdrant);

        await service.ProcessAsync(document.Id, Lease);

        var stored = await LoadAsync(document.Id);
        Assert.Equal(StolenLease, stored.ProcessingLeaseId);
        Assert.Equal("Extracting", stored.IndexingStatus);
        Assert.Equal(0, stored.ProcessingAttemptCount);
        Assert.Null(stored.IndexingError);
    }

    [Fact]
    public async Task ProcessAsync_RenewsHeartbeatWhileEmbeddingsAreGenerated()
    {
        var document = await AddDocumentAsync();
        var observedHeartbeats = new List<DateTime?>();
        var ollama = new FakeOllamaService
        {
            OnEmbedding = async _ =>
            {
                await using var observer = NewContext();
                observedHeartbeats.Add(await observer.Documents
                    .Where(item => item.Id == document.Id)
                    .Select(item => item.ProcessingHeartbeatAt)
                    .SingleAsync());
                await Task.Delay(5);
            }
        };
        var service = CreateService(
            new FakeDocumentProcessor(Chunks(document.Id, 12)),
            new FakeQdrantService(),
            ollama: ollama);

        await service.ProcessAsync(document.Id, Lease);

        Assert.True(
            observedHeartbeats.Distinct().Count() >= 3,
            "Her embedding grubundan sonra heartbeat yenilenmeli.");
        Assert.Equal("Ready", (await LoadAsync(document.Id)).IndexingStatus);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsDocumentToQueueOnShutdown()
    {
        var document = await AddDocumentAsync();
        using var shutdown = new CancellationTokenSource();
        var ollama = new FakeOllamaService
        {
            OnEmbedding = _ =>
            {
                shutdown.Cancel();
                return Task.CompletedTask;
            }
        };
        var service = CreateService(
            new FakeDocumentProcessor(Chunks(document.Id, 8)),
            new FakeQdrantService(),
            ollama: ollama);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ProcessAsync(document.Id, Lease, shutdown.Token));

        var stored = await LoadAsync(document.Id);
        Assert.Equal("Pending", stored.IndexingStatus);
        Assert.Null(stored.ProcessingLeaseId);
        Assert.Equal(8, stored.Chunks.Count);
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    // Gerçek bir devralmayı canlandırır: başka bir worker süre aşımıyla belgeyi
    // yeni bir lease ile sahiplenir.
    private async Task StealLeaseAsync(int documentId)
    {
        await using var otherWorker = NewContext();
        await otherWorker.Documents
            .Where(item => item.Id == documentId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.ProcessingLeaseId, StolenLease)
                .SetProperty(item => item.IndexingStatus, "Extracting")
                .SetProperty(item => item.ProcessingHeartbeatAt, DateTime.UtcNow));
    }

    private async Task<Document> LoadAsync(int documentId)
    {
        await using var reader = NewContext();
        return await reader.Documents
            .AsNoTracking()
            .Include(item => item.Chunks)
            .SingleAsync(item => item.Id == documentId);
    }

    private static Chunk[] Chunks(int documentId, int count) =>
        Enumerable.Range(0, count)
            .Select(index => new Chunk
            {
                DocumentId = documentId,
                ChunkIndex = index,
                Content = $"metin {index}",
                PageNumber = 1
            })
            .ToArray();

    private DocumentIndexingService CreateService(
        IDocumentProcessor processor,
        IQdrantService qdrant,
        int maxAttempts = 1,
        IOllamaService? ollama = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DocumentIndexingSettings:MaxAttempts"] = maxAttempts.ToString(),
                ["DocumentIndexingSettings:RetryDelaySeconds"] = "5",
                ["DocumentIndexingSettings:HeartbeatSeconds"] = "0"
            })
            .Build();

        return new(
            _context,
            processor,
            ollama ?? new FakeOllamaService(),
            qdrant,
            configuration,
            NullLogger<DocumentIndexingService>.Instance);
    }

    private async Task<Document> AddDocumentAsync()
    {
        var now = DateTime.UtcNow;
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
            UploadDate = now,
            IndexingStatus = "Extracting",
            ProcessingStartedAt = now,
            ProcessingHeartbeatAt = now,
            ProcessingLeaseId = Lease
        };
        _context.Documents.Add(document);
        await _context.SaveChangesAsync();
        return document;
    }

    private sealed class FakeDocumentProcessor(params Chunk[] chunks) : IDocumentProcessor
    {
        public Func<Task>? OnProcess { get; init; }
        public int Calls { get; private set; }

        public async Task<List<Chunk>> ProcessPdfAsync(
            Document document,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (OnProcess is not null)
            {
                await OnProcess();
            }

            return chunks.ToList();
        }
    }

    private sealed class FakeOllamaService : IOllamaService
    {
        private int _calls;

        public Func<int, Task>? OnEmbedding { get; init; }
        public int Calls => _calls;

        public Task WarmupAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task<float[]> GetEmbeddingAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);
            if (OnEmbedding is not null)
            {
                await OnEmbedding(call);
            }

            return [0.1f, 0.2f, 0.3f];
        }

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
        public Func<Task>? SaveHandler { get; init; }
        public int SaveCalls { get; private set; }
        public Func<int, string, CancellationToken, Task>? DeleteExceptVersionHandler { get; init; }

        public Task CreateCollectionIfNotExistsAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task SaveChunksAsync(
            List<Chunk> chunks,
            List<float[]> embeddings,
            string indexVersion,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            if (SaveHandler is not null)
            {
                await SaveHandler();
            }

            if (Exception is not null)
            {
                throw Exception;
            }
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
