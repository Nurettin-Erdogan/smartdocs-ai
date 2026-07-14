using SmartDocsAI.API.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmartDocsAI.API.Interfaces
{
    public interface IDocumentProcessor
    {
        /// <summary>
        /// Yüklenen belgenin (PDF) içeriğini okur, parçalara (Chunk) ayırır ve Chunk listesini döner.
        /// </summary>
        Task<List<Chunk>> ProcessPdfAsync(Document document, CancellationToken cancellationToken = default);
    }
}
