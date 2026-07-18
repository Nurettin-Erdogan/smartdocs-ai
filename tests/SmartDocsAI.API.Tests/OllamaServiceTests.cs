using Microsoft.Extensions.Configuration;
using SmartDocsAI.API.Services;

namespace SmartDocsAI.API.Tests;

public sealed class OllamaServiceTests
{
    [Fact]
    public async Task GetEmbeddingAsync_ReturnsValidEmbedding()
    {
        var service = CreateService(_ =>
            TestHttpMessageHandler.Json("{\"embeddings\":[[0.1,0.2,0.3]]}"));

        var embedding = await service.GetEmbeddingAsync("örnek metin");

        Assert.Equal(3, embedding.Length);
        Assert.Equal(0.2f, embedding[1]);
    }

    [Fact]
    public async Task GetEmbeddingAsync_RejectsEmptyEmbedding()
    {
        var service = CreateService(_ => TestHttpMessageHandler.Json("{\"embeddings\":[]}"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetEmbeddingAsync("örnek metin"));

        Assert.Contains("embedding", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAnswerAsync_RejectsWhitespaceAnswer()
    {
        var service = CreateService(_ => TestHttpMessageHandler.Json("{\"response\":\"   \"}"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateAnswerAsync("soru"));
    }

    [Fact]
    public async Task GenerateAnswerAsync_UsesUnlimitedOutputAndKeepsModelLoaded()
    {
        string? requestBody = null;
        var service = CreateService(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return TestHttpMessageHandler.Json("{\"response\":\"yanıt\"}");
        });

        await service.GenerateAnswerAsync("soru");

        Assert.Contains("\"num_predict\":-1", requestBody);
        Assert.Contains("\"keep_alive\":-1", requestBody);
        Assert.Contains("\"num_ctx\":4096", requestBody);
    }

    [Fact]
    public async Task StreamAnswerAsync_ReturnsChunksInOrder()
    {
        var service = CreateService(_ => TestHttpMessageHandler.Json(
            "{\"response\":\"Merhaba\",\"done\":false}\n" +
            "{\"response\":\" dünya\",\"done\":false}\n" +
            "{\"response\":\"!\",\"done\":true}\n"));

        var chunks = new List<string>();
        await foreach (var chunk in service.StreamAnswerAsync("soru"))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(["Merhaba", " dünya", "!"], chunks);
    }

    private static OllamaService CreateService(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OllamaSettings:BaseUrl"] = "http://ollama.test",
                ["OllamaSettings:EmbeddingModel"] = "embedding-test",
                ["OllamaSettings:ChatModel"] = "chat-test"
            })
            .Build();

        return new OllamaService(
            new HttpClient(new TestHttpMessageHandler(handler)),
            configuration);
    }
}
