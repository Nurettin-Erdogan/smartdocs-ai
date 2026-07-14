using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using SmartDocsAI.API.Controllers;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Models;

namespace SmartDocsAI.API.Tests;

public sealed class DocumentsControllerTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _context = null!;
    private readonly FakeDeletionService _deletionService = new();

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
    public async Task DeleteDocument_WhenCleanupFails_PersistsDeletingStateForRetry()
    {
        var document = await AddDocumentAsync("Failed");
        _deletionService.Exception = new HttpRequestException("Qdrant offline");
        var controller = CreateController(document.UserId);

        var result = await controller.DeleteDocument(document.Id, CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        _context.ChangeTracker.Clear();
        var stored = await _context.Documents.SingleAsync(item => item.Id == document.Id);
        Assert.Equal("Deleting", stored.IndexingStatus);
        Assert.Contains("offline", stored.IndexingError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteDocument_WhenIndexing_ReturnsConflictWithoutCleanup()
    {
        var document = await AddDocumentAsync("Pending");
        var controller = CreateController(document.UserId);

        var result = await controller.DeleteDocument(document.Id, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Empty(_deletionService.DocumentIds);
    }

    [Fact]
    public async Task ReindexDocument_WhenDeletionIsPending_ReturnsConflict()
    {
        var document = await AddDocumentAsync("Deleting");
        var controller = CreateController(document.UserId);

        var result = await controller.ReindexDocument(document.Id, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task DeleteDocument_DoesNotRevealAnotherUsersDocument()
    {
        var document = await AddDocumentAsync("Ready");
        var controller = CreateController(document.UserId + 1);

        var result = await controller.DeleteDocument(document.Id, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Empty(_deletionService.DocumentIds);
    }

    private DocumentsController CreateController(int userId)
    {
        var controller = new DocumentsController(
            _context,
            new FakeEnvironment(),
            new FakeDocumentProcessor(),
            new FakeOllamaService(),
            new FakeQdrantService(),
            _deletionService,
            NullLogger<DocumentsController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                    "test"))
            }
        };
        return controller;
    }

    private async Task<Document> AddDocumentAsync(string status)
    {
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
            FilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf"),
            FileSize = 10,
            UploadDate = DateTime.UtcNow,
            IndexingStatus = status
        };
        _context.Documents.Add(document);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return document;
    }

    private sealed class FakeDeletionService : IDocumentDeletionService
    {
        public Exception? Exception { get; set; }
        public List<int> DocumentIds { get; } = [];

        public Task<bool> DeleteAsync(int documentId, CancellationToken cancellationToken = default)
        {
            DocumentIds.Add(documentId);
            return Exception is null
                ? Task.FromResult(true)
                : Task.FromException<bool>(Exception);
        }
    }

    private sealed class FakeDocumentProcessor : IDocumentProcessor
    {
        public Task<List<Chunk>> ProcessPdfAsync(Document document, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<Chunk>());
    }

    private sealed class FakeOllamaService : IOllamaService
    {
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(new[] { 0.1f, 0.2f, 0.3f });

        public Task<string> GenerateAnswerAsync(string prompt, CancellationToken cancellationToken = default) =>
            Task.FromResult("answer");
    }

    private sealed class FakeQdrantService : IQdrantService
    {
        public Task CreateCollectionIfNotExistsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveChunksAsync(List<Chunk> chunks, List<float[]> embeddings, string indexVersion, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteDocumentChunksAsync(int documentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteDocumentChunksExceptVersionAsync(int documentId, string indexVersion, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<List<QdrantSearchResult>> SearchSimilarChunksAsync(float[] queryVector, int limit, IReadOnlyDictionary<int, string?> documentVersions, double minimumScore, CancellationToken cancellationToken = default) => Task.FromResult(new List<QdrantSearchResult>());
    }

    private sealed class FakeEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
