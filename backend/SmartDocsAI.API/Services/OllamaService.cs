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
        private readonly string _embeddingModel;
        private readonly string _chatModel;

        public OllamaService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _config = config;

            // appsettings.json'dan Ollama bağlantı adresini alıyoruz.
            var baseUrl = _config["OllamaSettings:BaseUrl"] ?? "http://localhost:11434";
            _httpClient.BaseAddress = new Uri(baseUrl);

            // Embedding ve yanıt üretimi için ayrı modeller kullanılabilir.
            _embeddingModel = _config["OllamaSettings:EmbeddingModel"] ?? "nomic-embed-text";
            _chatModel = _config["OllamaSettings:ChatModel"] ?? "llama3";
        }

        public async Task<float[]> GetEmbeddingAsync(string text)
        {
            var requestBody = new
            {
                model = _embeddingModel,
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

        public async Task<string> GenerateAnswerAsync(string prompt)
        {
            var requestBody = new
            {
                model = _chatModel,
                prompt = prompt,
                stream = false
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync("/api/generate", jsonContent);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<OllamaGenerateResponse>(responseString);

            return result?.Response ?? throw new Exception("Ollama'dan yanıt alınamadı.");
        }
    }

    public class OllamaEmbeddingResponse
    {
        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = Array.Empty<float>();
    }

    public class OllamaGenerateResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;
    }
}
