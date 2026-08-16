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
        Task CreateCollectionIfNotExistsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// PDF parçalarını (Chunks) ve bu parçaların embedding vektörlerini Qdrant'a kaydeder.
        /// </summary>
        Task SaveChunksAsync(
            List<Chunk> chunks,
            List<float[]> embeddings,
            string indexVersion,
            CancellationToken cancellationToken = default);

        Task DeleteDocumentChunksAsync(int documentId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Keeps the freshly written version and removes older vectors for the document.
        /// </summary>
        Task DeleteDocumentChunksExceptVersionAsync(
            int documentId,
            string indexVersion,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Soru vektörüne en yakın (benzer) metin parçalarını Qdrant'tan getirir.
        /// </summary>
        Task<List<QdrantSearchResult>> SearchSimilarChunksAsync(
            float[] queryVector,
            int limit,
            IReadOnlyDictionary<int, string?> documentVersions,
            double minimumScore,
            CancellationToken cancellationToken = default);
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
