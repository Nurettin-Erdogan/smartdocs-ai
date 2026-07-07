using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SmartDocsAI.API.Interfaces;

namespace SmartDocsAI.API.Services
{
    public class OllamaService : IOllamaService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly string _model;

        public OllamaService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _config = config;
            
            // appsettings.json'dan Ollama bağlantı adresini alıyoruz.
            var baseUrl = _config["OllamaSettings:BaseUrl"] ?? "http://localhost:11434";
            _httpClient.BaseAddress = new Uri(baseUrl);
            
            // Embedding için kullanılacak yerel modeli seçiyoruz (Örn: nomic-embed-text)
            _model = _config["OllamaSettings:EmbeddingModel"] ?? "nomic-embed-text";
        }

        public async Task<float[]> GetEmbeddingAsync(string text)
        {
            var requestBody = new
            {
                model = _model,
                prompt = text
            };

            // İsteği JSON formatına serileştiriyoruz.
            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody), 
                Encoding.UTF8, 
                "application/json"
            );

            // Ollama yerel servisinin /api/embeddings ucuna istek atıyoruz.
            var response = await _httpClient.PostAsync("/api/embeddings", jsonContent);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            
            // Gelen cevabı C# nesnesine dönüştürüyoruz (De-serileştirme).
            var result = JsonSerializer.Deserialize<OllamaEmbeddingResponse>(responseString);
            
            return result?.Embedding ?? throw new Exception("Ollama'dan embedding alınamadı.");
        }
    }

    public class OllamaEmbeddingResponse
    {
        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = Array.Empty<float>();
    }
}
