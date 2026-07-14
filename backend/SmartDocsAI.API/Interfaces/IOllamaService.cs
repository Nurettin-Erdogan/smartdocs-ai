using System.Threading.Tasks;

namespace SmartDocsAI.API.Interfaces
{
    public interface IOllamaService
    {
        /// <summary>
        /// Verilen metni Ollama API'si üzerinden vektör (embedding) değerlerine dönüştürür.
        /// </summary>
        Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default);

        /// <summary>
        /// Verilen prompt için Ollama üzerinden metin cevabı üretir.
        /// </summary>
        Task<string> GenerateAnswerAsync(string prompt, CancellationToken cancellationToken = default);
    }
}
