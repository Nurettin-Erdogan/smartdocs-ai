using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Models;

namespace SmartDocsAI.API.Services;

public sealed class QdrantService : IQdrantService
{
    private readonly HttpClient _httpClient;
    private readonly string _collectionName;
    private readonly int _vectorSize;
    private readonly int _upsertBatchSize;

    public QdrantService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(
            configuration["QdrantSettings:BaseUrl"] ?? "http://localhost:6333");

        _collectionName = configuration["QdrantSettings:CollectionName"] ?? "smartdocs_chunks";
        _vectorSize = Math.Clamp(
            configuration.GetValue<int?>("QdrantSettings:VectorSize") ?? 768,
            1,
            65_536);
        _upsertBatchSize = Math.Clamp(
            configuration.GetValue<int?>("QdrantSettings:UpsertBatchSize") ?? 64,
            1,
            256);
    }

    public async Task CreateCollectionIfNotExistsAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"/collections/{_collectionName}",
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }

        using var createResponse = await _httpClient.PutAsJsonAsync(
            $"/collections/{_collectionName}",
            new { vectors = new { size = _vectorSize, distance = "Cosine" } },
            cancellationToken);

        createResponse.EnsureSuccessStatusCode();
    }

    public async Task SaveChunksAsync(
        List<Chunk> chunks,
        List<float[]> embeddings,
        string indexVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexVersion);

        if (chunks.Count != embeddings.Count)
        {
            throw new ArgumentException("Metin parçaları ve embedding sayıları eşleşmiyor.");
        }

        if (chunks.Count == 0)
        {
            return;
        }

        foreach (var embedding in embeddings)
        {
            if (embedding.Length != _vectorSize || embedding.Any(value => !float.IsFinite(value)))
            {
                throw new InvalidOperationException(
                    $"Embedding boyutu {_vectorSize} olmalı ve yalnızca sonlu değerler içermelidir.");
            }
        }

        await CreateCollectionIfNotExistsAsync(cancellationToken);

        for (var offset = 0; offset < chunks.Count; offset += _upsertBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(_upsertBatchSize, chunks.Count - offset);
            var points = new List<object>(count);

            for (var index = offset; index < offset + count; index++)
            {
                var chunk = chunks[index];
                points.Add(new
                {
                    id = CreatePointId(chunk.DocumentId, chunk.ChunkIndex, indexVersion),
                    vector = embeddings[index],
                    payload = new
                    {
                        documentId = chunk.DocumentId,
                        chunkIndex = chunk.ChunkIndex,
                        content = chunk.Content,
                        pageNumber = chunk.PageNumber,
                        indexVersion
                    }
                });
            }

            using var response = await _httpClient.PutAsJsonAsync(
                $"/collections/{_collectionName}/points?wait=true",
                new { points },
                cancellationToken);

            response.EnsureSuccessStatusCode();
        }
    }

    public async Task DeleteDocumentChunksAsync(
        int documentId,
        CancellationToken cancellationToken = default)
    {
        if (!await CollectionExistsAsync(cancellationToken))
        {
            return;
        }

        await DeleteByFilterAsync(
            new
            {
                must = new object[]
                {
                    new { key = "documentId", match = new { value = documentId } }
                }
            },
            cancellationToken);
    }

    public async Task DeleteDocumentChunksExceptVersionAsync(
        int documentId,
        string indexVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexVersion);

        if (!await CollectionExistsAsync(cancellationToken))
        {
            return;
        }

        await DeleteByFilterAsync(
            new
            {
                must = new object[]
                {
                    new { key = "documentId", match = new { value = documentId } }
                },
                must_not = new object[]
                {
                    new { key = "indexVersion", match = new { value = indexVersion } }
                }
            },
            cancellationToken);
    }

    public async Task<List<QdrantSearchResult>> SearchSimilarChunksAsync(
        float[] queryVector,
        int limit,
        List<int> documentIds,
        double minimumScore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryVector);
        ArgumentNullException.ThrowIfNull(documentIds);

        if (queryVector.Length != _vectorSize)
        {
            throw new InvalidOperationException($"Sorgu embedding boyutu {_vectorSize} olmalıdır.");
        }

        if (documentIds.Count == 0)
        {
            return new List<QdrantSearchResult>();
        }

        limit = Math.Clamp(limit, 1, 20);
        minimumScore = Math.Clamp(minimumScore, 0, 1);
        var requestedLimit = Math.Min(limit * 3, 50);

        var searchRequest = new
        {
            query = queryVector,
            limit = requestedLimit,
            score_threshold = minimumScore,
            with_payload = true,
            filter = new
            {
                must = new object[]
                {
                    new
                    {
                        key = "documentId",
                        match = new { any = documentIds.Distinct().ToArray() }
                    }
                }
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(
            $"/collections/{_collectionName}/points/query",
            searchRequest,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var searchResponse = await response.Content.ReadFromJsonAsync<QdrantQueryResponse>(
            cancellationToken: cancellationToken);

        return (searchResponse?.Result.Points ?? new List<QdrantResultItem>())
            .Where(item => item.Payload is not null && item.Score >= minimumScore)
            .Select(item => new QdrantSearchResult
            {
                DocumentId = item.Payload!.DocumentId,
                ChunkIndex = item.Payload.ChunkIndex,
                Content = item.Payload.Content,
                PageNumber = item.Payload.PageNumber,
                Score = item.Score
            })
            .Where(item => documentIds.Contains(item.DocumentId))
            .GroupBy(item => (item.DocumentId, item.ChunkIndex))
            .Select(group => group.OrderByDescending(item => item.Score).First())
            .OrderByDescending(item => item.Score)
            .Take(limit)
            .ToList();
    }

    private async Task<bool> CollectionExistsAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"/collections/{_collectionName}",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    private async Task DeleteByFilterAsync(object filter, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"/collections/{_collectionName}/points/delete?wait=true",
            new { filter },
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static string CreatePointId(int documentId, int chunkIndex, string indexVersion)
    {
        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{documentId}:{chunkIndex}:{indexVersion}"));
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }
}

public sealed class QdrantQueryResponse
{
    [JsonPropertyName("result")]
    public QdrantQueryResult Result { get; init; } = new();
}

public sealed class QdrantQueryResult
{
    [JsonPropertyName("points")]
    public List<QdrantResultItem> Points { get; init; } = new();
}

public sealed class QdrantResultItem
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("score")]
    public double Score { get; init; }

    [JsonPropertyName("payload")]
    public QdrantPayload? Payload { get; init; }
}

public sealed class QdrantPayload
{
    [JsonPropertyName("documentId")]
    public int DocumentId { get; init; }

    [JsonPropertyName("chunkIndex")]
    public int ChunkIndex { get; init; }

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    [JsonPropertyName("pageNumber")]
    public int PageNumber { get; init; }
}
