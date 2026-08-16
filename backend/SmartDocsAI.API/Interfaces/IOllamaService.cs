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
        /// Sohbet ve embedding modellerini ilk kullanıcı isteğinden önce belleğe yükler.
        /// </summary>
        Task WarmupAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Verilen prompt için Ollama üzerinden metin cevabı üretir.
        /// </summary>
        Task<string> GenerateAnswerAsync(string prompt, CancellationToken cancellationToken = default);

        /// <summary>
        /// Cevabı üretildikçe parça parça döndürür.
        /// </summary>
        IAsyncEnumerable<string> StreamAnswerAsync(
            string prompt,
            CancellationToken cancellationToken = default);
    }
}
