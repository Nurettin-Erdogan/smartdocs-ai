using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Models;

namespace SmartDocsAI.API.Services
{
    public class QdrantService : IQdrantService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly string _collectionName;
        private readonly int _vectorSize;

        public QdrantService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _config = config;

            // appsettings.json'dan Qdrant adresi ve koleksiyon ayarlarını alıyoruz.
            var baseUrl = _config["QdrantSettings:BaseUrl"] ?? "http://localhost:6333";
            _httpClient.BaseAddress = new Uri(baseUrl);

            _collectionName = _config["QdrantSettings:CollectionName"] ?? "smartdocs_chunks";
            
            // nomic-embed-text modelinin vektör boyutu 768'dir.
            _vectorSize = int.Parse(_config["QdrantSettings:VectorSize"] ?? "768");
        }

        public async Task CreateCollectionIfNotExistsAsync()
        {
            // Koleksiyonun var olup olmadığını kontrol etmek için GET isteği atıyoruz.
            var response = await _httpClient.GetAsync($"/collections/{_collectionName}");
            
            if (response.IsSuccessStatusCode)
            {
                // Koleksiyon zaten var, işlem yapmaya gerek yok.
                return;
            }

            // Koleksiyon yoksa, PUT isteği ile oluşturuyoruz.
            var createRequest = new
            {
                vectors = new
                {
                    size = _vectorSize,
                    distance = "Cosine" // Kosinüs benzerliği
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(createRequest),
                Encoding.UTF8,
                "application/json"
            );

            var createResponse = await _httpClient.PutAsync($"/collections/{_collectionName}", jsonContent);
            createResponse.EnsureSuccessStatusCode();
        }

        public async Task SaveChunksAsync(List<Chunk> chunks, List<float[]> embeddings)
        {
            if (chunks.Count != embeddings.Count)
            {
                throw new ArgumentException("Metin parçacıkları ve vektörlerin sayıları eşleşmiyor.");
            }

            // Koleksiyonun mevcut olduğundan emin oluyoruz.
            await CreateCollectionIfNotExistsAsync();

            var points = new List<object>();

            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var embedding = embeddings[i];

                points.Add(new
                {
                    id = Guid.NewGuid().ToString(), // Qdrant benzersiz ID ister (UUID formatında).
                    vector = embedding,
                    payload = new
                    {
                        documentId = chunk.DocumentId,
                        chunkIndex = chunk.ChunkIndex,
                        content = chunk.Content,
                        pageNumber = chunk.PageNumber
                    }
                });
            }

            var upsertRequest = new { points };

            // Qdrant'a toplu ekleme (Upsert) isteği atıyoruz.
            var response = await _httpClient.PutAsJsonAsync($"/collections/{_collectionName}/points?wait=true", upsertRequest);
            response.EnsureSuccessStatusCode();
        }

        public async Task<List<QdrantSearchResult>> SearchSimilarChunksAsync(float[] queryVector, int limit = 3)
        {
            var searchRequest = new
            {
                vector = queryVector,
                limit = limit,
                with_payload = true
            };

            // Qdrant /collections/{name}/points/search ucuna arama isteği atıyoruz.
            var response = await _httpClient.PostAsJsonAsync($"/collections/{_collectionName}/points/search", searchRequest);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            var searchResponse = JsonSerializer.Deserialize<QdrantSearchResponse>(responseString);

            var results = new List<QdrantSearchResult>();

            if (searchResponse?.Result != null)
            {
                foreach (var item in searchResponse.Result)
                {
                    if (item.Payload != null)
                    {
                        results.Add(new QdrantSearchResult
                        {
                            DocumentId = item.Payload.DocumentId,
                            ChunkIndex = item.Payload.ChunkIndex,
                            Content = item.Payload.Content,
                            PageNumber = item.Payload.PageNumber,
                            Score = item.Score
                        });
                    }
                }
            }

            return results;
        }
    }

    #region JSON Serileştirme Yardımcı Sınıfları

    public class QdrantSearchResponse
    {
        [JsonPropertyName("result")]
        public List<QdrantResultItem> Result { get; set; } = new();
    }

    public class QdrantResultItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("payload")]
        public QdrantPayload? Payload { get; set; }
    }

    public class QdrantPayload
    {
        [JsonPropertyName("documentId")]
        public int DocumentId { get; set; }

        [JsonPropertyName("chunkIndex")]
        public int ChunkIndex { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("pageNumber")]
        public int PageNumber { get; set; }
    }

    #endregion
}
