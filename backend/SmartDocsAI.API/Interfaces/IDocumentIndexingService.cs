namespace SmartDocsAI.API.Interfaces;

public interface IDocumentIndexingService
{
    Task ProcessAsync(int documentId, CancellationToken cancellationToken = default);
}
