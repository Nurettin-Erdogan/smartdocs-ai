using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartDocsAI.API.Interfaces;

namespace SmartDocsAI.API.Services;

public sealed class OllamaService : IOllamaService
{
    private readonly HttpClient _httpClient;
    private readonly string _embeddingModel;
    private readonly string _chatModel;
    private readonly object _keepAlive;
    private readonly int _numContext;
    private readonly double _temperature;

    public OllamaService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(
            configuration["OllamaSettings:BaseUrl"] ?? "http://localhost:11434");

        _embeddingModel = configuration["OllamaSettings:EmbeddingModel"] ?? "nomic-embed-text";
        _chatModel = configuration["OllamaSettings:ChatModel"] ?? "qwen2.5:3b";
        var configuredKeepAlive = configuration["OllamaSettings:KeepAlive"] ?? "-1";
        _keepAlive = int.TryParse(configuredKeepAlive, out var keepAliveNumber)
            ? keepAliveNumber
            : configuredKeepAlive;
        _numContext = Math.Clamp(
            configuration.GetValue<int?>("OllamaSettings:NumContext") ?? 4096,
            2048,
            32768);
        _temperature = Math.Clamp(
            configuration.GetValue<double?>("OllamaSettings:Temperature") ?? 0.1,
            0,
            2);
    }

    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "/api/generate",
            new
            {
                model = _chatModel,
                prompt = string.Empty,
                stream = false,
                keep_alive = _keepAlive
            },
            cancellationToken);
        response.EnsureSuccessStatusCode();

        using var embeddingResponse = await _httpClient.PostAsJsonAsync(
            "/api/embed",
            new
            {
                model = _embeddingModel,
                input = "hazırlık",
                truncate = true,
                keep_alive = _keepAlive
            },
            cancellationToken);
        embeddingResponse.EnsureSuccessStatusCode();
    }

    public async Task<float[]> GetEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        using var response = await _httpClient.PostAsJsonAsync(
            "/api/embed",
            new
            {
                model = _embeddingModel,
                input = text,
                truncate = true,
                keep_alive = _keepAlive
            },
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
                keep_alive = _keepAlive,
                options = new
                {
                    num_predict = -1,
                    num_ctx = _numContext,
                    temperature = _temperature
                }
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

        return answer;
    }

    public async IAsyncEnumerable<string> StreamAnswerAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/generate")
        {
            Content = JsonContent.Create(new
            {
                model = _chatModel,
                prompt,
                stream = true,
                keep_alive = _keepAlive,
                options = new
                {
                    num_predict = -1,
                    num_ctx = _numContext,
                    temperature = _temperature
                }
            })
        };
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var producedContent = false;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var chunk = JsonSerializer.Deserialize<OllamaGenerateStreamResponse>(line);
            if (!string.IsNullOrWhiteSpace(chunk?.Error))
            {
                throw new InvalidOperationException($"Ollama akış hatası: {chunk.Error}");
            }

            if (!string.IsNullOrEmpty(chunk?.Response))
            {
                producedContent = true;
                yield return chunk.Response;
            }

            if (chunk?.Done == true)
            {
                break;
            }
        }

        if (!producedContent)
        {
            throw new InvalidOperationException("Ollama boş bir cevap döndürdü.");
        }
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

public sealed class OllamaGenerateStreamResponse
{
    [JsonPropertyName("response")]
    public string? Response { get; init; }

    [JsonPropertyName("done")]
    public bool Done { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
