using SmartDocsAI.API.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmartDocsAI.API.Interfaces
{
    public interface IQdrantService
    {
        /// <summary>
        /// Qdrant üzerinde vektörlerin saklanacağı koleksiyonu (varsa geçip, yoksa) oluşturur.
        /// </summary>
        Task CreateCollectionIfNotExistsAsync();

        /// <summary>
        /// PDF parçalarını (Chunks) ve bu parçaların embedding vektörlerini Qdrant'a kaydeder.
        /// </summary>
        Task SaveChunksAsync(List<Chunk> chunks, List<float[]> embeddings);

        /// <summary>
        /// Soru vektörüne en yakın (benzer) metin parçalarını Qdrant'tan getirir.
        /// </summary>
        Task<List<QdrantSearchResult>> SearchSimilarChunksAsync(float[] queryVector, int limit = 3, List<int>? documentIds = null);
    }

    /// <summary>
    /// Qdrant anlamsal arama sonuç modelidir.
    /// </summary>
    public class QdrantSearchResult
    {
        public int DocumentId { get; set; }
        public int ChunkIndex { get; set; }
        public string Content { get; set; } = string.Empty;
        public int PageNumber { get; set; }
        public double Score { get; set; } // Benzerlik skoru (Kosinüs benzerliği)
    }
}
