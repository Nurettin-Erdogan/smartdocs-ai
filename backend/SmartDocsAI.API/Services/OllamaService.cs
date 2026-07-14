using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SmartDocsAI.API.Interfaces;

namespace SmartDocsAI.API.Services;

public sealed class OllamaService : IOllamaService
{
    private readonly HttpClient _httpClient;
    private readonly string _embeddingModel;
    private readonly string _chatModel;
    private readonly int _maxAnswerTokens;
    private readonly int _maxAnswerCharacters;

    public OllamaService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(
            configuration["OllamaSettings:BaseUrl"] ?? "http://localhost:11434");

        _embeddingModel = configuration["OllamaSettings:EmbeddingModel"] ?? "nomic-embed-text";
        _chatModel = configuration["OllamaSettings:ChatModel"] ?? "llama3";
        _maxAnswerTokens = Math.Clamp(
            configuration.GetValue<int?>("OllamaSettings:MaxAnswerTokens") ?? 768,
            64,
            4_096);
        _maxAnswerCharacters = Math.Clamp(
            configuration.GetValue<int?>("OllamaSettings:MaxAnswerCharacters") ?? 20_000,
            1_000,
            100_000);
    }

    public async Task<float[]> GetEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        using var response = await _httpClient.PostAsJsonAsync(
            "/api/embed",
            new { model = _embeddingModel, input = text, truncate = true },
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(
            cancellationToken: cancellationToken);

        if (result?.Embeddings is not { Length: > 0 } embeddings ||
            embeddings[0] is not { Length: > 0 } embedding ||
            embedding.Any(value => !float.IsFinite(value)))
        {
            throw new InvalidOperationException("Ollama geçerli bir embedding döndürmedi.");
        }

        return embedding;
    }

    public async Task<string> GenerateAnswerAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        using var response = await _httpClient.PostAsJsonAsync(
            "/api/generate",
            new
            {
                model = _chatModel,
                prompt,
                stream = false,
                options = new { num_predict = _maxAnswerTokens }
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(
            cancellationToken: cancellationToken);
        var answer = result?.Response?.Trim();

        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new InvalidOperationException("Ollama boş bir cevap döndürdü.");
        }

        if (answer.Length > _maxAnswerCharacters)
        {
            throw new InvalidOperationException(
                "Ollama güvenli cevap uzunluğu sınırını aştı.");
        }

        return answer;
    }
}

public sealed class OllamaEmbeddingResponse
{
    [JsonPropertyName("embeddings")]
    public float[][]? Embeddings { get; init; }
}

public sealed class OllamaGenerateResponse
{
    [JsonPropertyName("response")]
    public string? Response { get; init; }
}
