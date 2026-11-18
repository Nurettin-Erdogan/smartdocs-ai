namespace SmartDocsAI.API.Interfaces;

public interface IDocumentIndexingService
{
    /// <summary>
    /// Sahiplenilmiş bir belgeyi işler. <paramref name="leaseId"/>, sahiplenme
    /// anında üretilen çit jetonudur; belge artık bu lease'e ait değilse hiçbir
    /// sonuç yazılmaz.
    /// </summary>
    Task ProcessAsync(int documentId, Guid leaseId, CancellationToken cancellationToken = default);
}
