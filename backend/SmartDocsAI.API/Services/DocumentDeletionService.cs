using Microsoft.EntityFrameworkCore;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.Interfaces;

namespace SmartDocsAI.API.Services;

public sealed class DocumentDeletionService : IDocumentDeletionService
{
    private readonly AppDbContext _context;
    private readonly IQdrantService _qdrantService;
    private readonly string _uploadsRoot;

    public DocumentDeletionService(
        AppDbContext context,
        IQdrantService qdrantService,
        IWebHostEnvironment environment)
    {
        _context = context;
        _qdrantService = qdrantService;
        _uploadsRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "Uploads"));
    }

    public async Task<bool> DeleteAsync(
        int documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents
            .AsNoTracking()
            .Where(item => item.Id == documentId && item.IndexingStatus == "Deleting")
            .Select(item => new { item.Id, item.FilePath })
            .FirstOrDefaultAsync(cancellationToken);

        if (document is null)
        {
            return false;
        }

        var filePath = Path.GetFullPath(document.FilePath);
        if (!IsWithinUploadsRoot(filePath))
        {
            throw new InvalidOperationException(
                $"Document {document.Id} has an unsafe storage path and was not deleted.");
        }

        await _qdrantService.DeleteDocumentChunksAsync(document.Id, cancellationToken);

        File.Delete(filePath);

        await _context.Documents
            .Where(item => item.Id == document.Id && item.IndexingStatus == "Deleting")
            .ExecuteDeleteAsync(cancellationToken);

        return true;
    }

    private bool IsWithinUploadsRoot(string filePath)
    {
        var relativePath = Path.GetRelativePath(_uploadsRoot, filePath);
        return relativePath != ".."
            && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathRooted(relativePath);
    }
}
