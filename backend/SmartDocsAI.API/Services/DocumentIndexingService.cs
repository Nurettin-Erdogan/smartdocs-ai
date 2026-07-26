using Microsoft.EntityFrameworkCore;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Models;

namespace SmartDocsAI.API.Services;

public sealed class DocumentIndexingService : IDocumentIndexingService
{
    private const int EmbeddingBatchSize = 4;

    private readonly AppDbContext _context;
    private readonly IDocumentProcessor _documentProcessor;
    private readonly IOllamaService _ollamaService;
    private readonly IQdrantService _qdrantService;
    private readonly ILogger<DocumentIndexingService> _logger;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;

    public DocumentIndexingService(
        AppDbContext context,
        IDocumentProcessor documentProcessor,
        IOllamaService ollamaService,
        IQdrantService qdrantService,
        IConfiguration configuration,
        ILogger<DocumentIndexingService> logger)
    {
        _context = context;
        _documentProcessor = documentProcessor;
        _ollamaService = ollamaService;
        _qdrantService = qdrantService;
        _logger = logger;
        _maxAttempts = Math.Clamp(
            configuration.GetValue<int?>("DocumentIndexingSettings:MaxAttempts") ?? 3,
            1,
            10);
        _retryDelay = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue<int?>("DocumentIndexingSettings:RetryDelaySeconds") ?? 15,
            5,
            3_600));
    }

    public async Task ProcessAsync(
        int documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents
            .Include(item => item.Chunks)
            .FirstOrDefaultAsync(item => item.Id == documentId, cancellationToken);

        if (document is null || document.IndexingStatus is not ("Extracting" or "Indexing"))
        {
            return;
        }

        try
        {
            var chunks = document.Chunks
                .OrderBy(chunk => chunk.ChunkIndex)
                .ToList();

            if (chunks.Count == 0)
            {
                document.IndexingStatus = "Extracting";
                await _context.SaveChangesAsync(cancellationToken);

                chunks = await _documentProcessor.ProcessPdfAsync(document, cancellationToken);
                if (chunks.Count == 0)
                {
                    document.IndexingStatus = "NoContent";
                    document.IndexingError = "Belgeden işlenecek metin çıkarılamadı.";
                    document.ProcessingStartedAt = null;
                    await _context.SaveChangesAsync(cancellationToken);
                    return;
                }

                _context.Chunks.AddRange(chunks);
                await _context.SaveChangesAsync(cancellationToken);
            }

            document.IndexingStatus = "Indexing";
            document.IndexingError = null;
            await _context.SaveChangesAsync(cancellationToken);

            var embeddings = await GenerateEmbeddingsInBatchesAsync(chunks, cancellationToken);
            var indexVersion = Guid.NewGuid().ToString("N");

            await _qdrantService.SaveChunksAsync(
                chunks,
                embeddings,
                indexVersion,
                cancellationToken);

            try
            {
                await _qdrantService.DeleteDocumentChunksExceptVersionAsync(
                    document.Id,
                    indexVersion,
                    cancellationToken);
            }
            catch (Exception cleanupException) when (
                cleanupException is not OperationCanceledException)
            {
                _logger.LogWarning(
                    cleanupException,
                    "Old Qdrant vectors for document {DocumentId} could not be cleaned up.",
                    document.Id);
            }

            document.IndexingStatus = "Ready";
            document.IndexingError = null;
            document.CurrentIndexVersion = indexVersion;
            document.ProcessingStartedAt = null;
            document.NextProcessingAttemptAt = null;
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Document {DocumentId} indexing failed.", documentId);
            document.ProcessingAttemptCount++;
            var retryable = document.CurrentIndexVersion is null &&
                document.ProcessingAttemptCount < _maxAttempts;
            document.IndexingStatus = document.CurrentIndexVersion is not null
                ? "Ready"
                : retryable ? "RetryWaiting" : "Failed";
            document.IndexingError = LimitError(exception);
            document.ProcessingStartedAt = null;
            document.NextProcessingAttemptAt = retryable
                ? DateTime.UtcNow + TimeSpan.FromTicks(
                    _retryDelay.Ticks * document.ProcessingAttemptCount)
                : null;
            await _context.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task<List<float[]>> GenerateEmbeddingsInBatchesAsync(
        List<Chunk> chunks,
        CancellationToken cancellationToken)
    {
        var embeddings = new List<float[]>(chunks.Count);

        foreach (var batch in chunks.Chunk(EmbeddingBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchEmbeddings = await Task.WhenAll(
                batch.Select(chunk =>
                    _ollamaService.GetEmbeddingAsync(chunk.Content, cancellationToken)));
            embeddings.AddRange(batchEmbeddings);
        }

        return embeddings;
    }

    private static string LimitError(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        return message[..Math.Min(1_000, message.Length)];
    }
}
