using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Models;
using SmartDocsAI.API.Services;

namespace SmartDocsAI.API.Tests;

public sealed class DocumentDeletionServiceTests : IAsyncLifetime
{
    private readonly string _contentRoot = Path.Combine(
        Path.GetTempPath(),
        $"smartdocs-deletion-{Guid.NewGuid():N}");
    private SqliteConnection _connection = null!;
    private AppDbContext _context = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_contentRoot, "Uploads"));
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
        Directory.Delete(_contentRoot, recursive: true);
    }

    [Fact]
    public async Task DeleteAsync_RemovesVectorFileAndDatabaseRecord()
    {
        var document = await AddDocumentAsync("Deleting");
        var qdrant = new FakeQdrantService();
        var service = CreateService(qdrant);

        var deleted = await service.DeleteAsync(document.Id);

        Assert.True(deleted);
        Assert.Equal(document.Id, Assert.Single(qdrant.DeletedDocumentIds));
        Assert.False(File.Exists(document.FilePath));
        Assert.False(await _context.Documents.AnyAsync(item => item.Id == document.Id));
    }

    [Fact]
    public async Task DeleteAsync_QdrantFailureKeepsDurableDeletionRecordAndFile()
    {
        var document = await AddDocumentAsync("Deleting");
        var qdrant = new FakeQdrantService { DeleteException = new HttpRequestException("offline") };
        var service = CreateService(qdrant);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.DeleteAsync(document.Id));

        _context.ChangeTracker.Clear();
        Assert.True(File.Exists(document.FilePath));
        Assert.Equal(
            "Deleting",
            (await _context.Documents.SingleAsync(item => item.Id == document.Id)).IndexingStatus);
    }

    [Fact]
    public async Task DeleteAsync_RejectsPathOutsideUploadsBeforeExternalCleanup()
    {
        var outsidePath = Path.Combine(_contentRoot, "outside.pdf");
        await File.WriteAllTextAsync(outsidePath, "sensitive");
        var document = await AddDocumentAsync("Deleting", outsidePath);
        var qdrant = new FakeQdrantService();
        var service = CreateService(qdrant);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(document.Id));

        Assert.Empty(qdrant.DeletedDocumentIds);
        Assert.True(File.Exists(outsidePath));
        Assert.True(await _context.Documents.AnyAsync(item => item.Id == document.Id));
    }

    private DocumentDeletionService CreateService(FakeQdrantService qdrant) => new(
        _context,
        qdrant,
        new FakeEnvironment { ContentRootPath = _contentRoot });

    private async Task<Document> AddDocumentAsync(string status, string? filePath = null)
    {
        filePath ??= Path.Combine(_contentRoot, "Uploads", $"{Guid.NewGuid():N}.pdf");
        if (!File.Exists(filePath))
        {
            await File.WriteAllTextAsync(filePath, "%PDF-test");
        }

        var user = new User
        {
            FullName = "Test User",
            Email = $"{Guid.NewGuid():N}@example.com",
            PasswordHash = "hash",
            RoleId = 2
        };
        var document = new Document
        {
            User = user,
            Title = "Test",
            FileName = "test.pdf",
            FileType = ".pdf",
            FilePath = filePath,
            FileSize = 9,
            UploadDate = DateTime.UtcNow,
            IndexingStatus = status
        };
        _context.Documents.Add(document);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return document;
    }

    private sealed class FakeEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FakeQdrantService : IQdrantService
    {
        public Exception? DeleteException { get; init; }
        public List<int> DeletedDocumentIds { get; } = [];

        public Task CreateCollectionIfNotExistsAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveChunksAsync(List<Chunk> chunks, List<float[]> embeddings, string indexVersion, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteDocumentChunksAsync(int documentId, CancellationToken cancellationToken = default)
        {
            if (DeleteException is not null)
            {
                return Task.FromException(DeleteException);
            }

            DeletedDocumentIds.Add(documentId);
            return Task.CompletedTask;
        }

        public Task DeleteDocumentChunksExceptVersionAsync(int documentId, string indexVersion, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<List<QdrantSearchResult>> SearchSimilarChunksAsync(float[] queryVector, int limit, IReadOnlyDictionary<int, string?> documentVersions, double minimumScore, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<QdrantSearchResult>());
    }
}
