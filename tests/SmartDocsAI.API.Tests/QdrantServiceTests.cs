using Microsoft.Extensions.Configuration;
using SmartDocsAI.API.Models;
using SmartDocsAI.API.Services;

namespace SmartDocsAI.API.Tests;

public sealed class QdrantServiceTests
{
    [Fact]
    public async Task SearchSimilarChunksAsync_AppliesThresholdAndDeduplicatesChunks()
    {
        string? requestBody = null;
        var service = CreateService(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return TestHttpMessageHandler.Json("""
                {
                  "result": {
                    "points": [
                      { "id": "1", "score": 0.91, "payload": { "documentId": 7, "chunkIndex": 2, "content": "iyi", "pageNumber": 3 } },
                      { "id": "2", "score": 0.81, "payload": { "documentId": 7, "chunkIndex": 2, "content": "eski kopya", "pageNumber": 3 } },
                      { "id": "3", "score": 0.20, "payload": { "documentId": 7, "chunkIndex": 4, "content": "ilgisiz", "pageNumber": 5 } },
                      { "id": "4", "score": 0.95, "payload": { "documentId": 99, "chunkIndex": 1, "content": "başka kullanıcı", "pageNumber": 1 } }
                    ]
                  }
                }
                """);
        });

        var results = await service.SearchSimilarChunksAsync(
            new[] { 0.1f, 0.2f, 0.3f },
            limit: 3,
            documentVersions: new Dictionary<int, string?> { [7] = "current-version" },
            minimumScore: 0.35);

        var result = Assert.Single(results);
        Assert.Equal("iyi", result.Content);
        Assert.Contains("\"score_threshold\":0.35", requestBody);
        Assert.Contains("\"query\":[0.1,0.2,0.3]", requestBody);
        Assert.Contains("\"documentId\"", requestBody);
        Assert.Contains("\"current-version\"", requestBody);
    }

    [Fact]
    public async Task SaveChunksAsync_RejectsUnexpectedVectorSizeBeforeCallingQdrant()
    {
        var calls = 0;
        var service = CreateService((_, _) =>
        {
            calls++;
            return Task.FromResult(TestHttpMessageHandler.Json("{}"));
        });
        var chunks = new List<Chunk>
        {
            new() { DocumentId = 1, ChunkIndex = 0, Content = "metin", PageNumber = 1 }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveChunksAsync(
                chunks,
                new List<float[]> { new[] { 0.1f, 0.2f } },
                "version-1"));

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task DeleteDocumentChunksExceptVersionAsync_KeepsFreshVersionInFilter()
    {
        string? deleteBody = null;
        var service = CreateService(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return TestHttpMessageHandler.Json("{}");
            }

            deleteBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return TestHttpMessageHandler.Json("{}");
        });

        await service.DeleteDocumentChunksExceptVersionAsync(42, "fresh-version");

        Assert.Contains("\"documentId\"", deleteBody);
        Assert.Contains("\"must_not\"", deleteBody);
        Assert.Contains("fresh-version", deleteBody);
    }

    private static QdrantService CreateService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["QdrantSettings:BaseUrl"] = "http://qdrant.test",
                ["QdrantSettings:CollectionName"] = "test_chunks",
                ["QdrantSettings:VectorSize"] = "3",
                ["QdrantSettings:UpsertBatchSize"] = "2"
            })
            .Build();

        return new QdrantService(
            new HttpClient(new TestHttpMessageHandler(handler)),
            configuration);
    }
}
