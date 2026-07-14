namespace SmartDocsAI.API.Interfaces;

public interface IDocumentDeletionService
{
    Task<bool> DeleteAsync(int documentId, CancellationToken cancellationToken = default);
}
