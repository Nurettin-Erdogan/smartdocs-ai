using Microsoft.EntityFrameworkCore;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Models;

namespace SmartDocsAI.API.Services;

public sealed class DocumentIndexingService : IDocumentIndexingService
{
    private const int EmbeddingBatchSize = 4;
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(5);

    private readonly AppDbContext _context;
    private readonly IDocumentProcessor _documentProcessor;
    private readonly IOllamaService _ollamaService;
    private readonly IQdrantService _qdrantService;
    private readonly ILogger<DocumentIndexingService> _logger;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;
    private readonly TimeSpan _heartbeatInterval;

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
        _heartbeatInterval = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue<int?>("DocumentIndexingSettings:HeartbeatSeconds") ?? 30,
            0,
            300));
    }

    public async Task ProcessAsync(
        int documentId,
        Guid leaseId,
        CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents
            .Include(item => item.Chunks)
            .FirstOrDefaultAsync(item => item.Id == documentId, cancellationToken);

        if (document is null ||
            document.ProcessingLeaseId != leaseId ||
            document.IndexingStatus is not ("Extracting" or "Indexing"))
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
                await SaveWithHeartbeatAsync(document, cancellationToken);

                chunks = await _documentProcessor.ProcessPdfAsync(document, cancellationToken);
                if (chunks.Count == 0)
                {
                    document.IndexingStatus = "NoContent";
                    document.IndexingError = "Belgeden işlenecek metin çıkarılamadı.";
                    ClearLease(document);
                    await _context.SaveChangesAsync(cancellationToken);
                    return;
                }

                // Parçalar ve belge aynı SaveChanges içinde yazılır: lease bu
                // arada başka bir worker'a geçtiyse işlem bütünüyle geri alınır ve
                // belgeye yinelenen parça eklenmez.
                _context.Chunks.AddRange(chunks);
                await SaveWithHeartbeatAsync(document, cancellationToken);
            }

            document.IndexingStatus = "Indexing";
            document.IndexingError = null;
            await SaveWithHeartbeatAsync(document, cancellationToken);

            var embeddings = await GenerateEmbeddingsInBatchesAsync(
                document,
                chunks,
                cancellationToken);
            var indexVersion = Guid.NewGuid().ToString("N");

            await _qdrantService.SaveChunksAsync(
                chunks,
                embeddings,
                indexVersion,
                cancellationToken);

            document.IndexingStatus = "Ready";
            document.IndexingError = null;
            document.CurrentIndexVersion = indexVersion;
            document.NextProcessingAttemptAt = null;
            ClearLease(document);
            await _context.SaveChangesAsync(cancellationToken);

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
        }
        catch (DbUpdateConcurrencyException)
        {
            // Sahiplik süre aşımıyla başka bir worker'a geçti. Belgenin durumu
            // artık onun sorumluluğunda; burada hiçbir şey yazılmaz.
            _context.ChangeTracker.Clear();
            _logger.LogWarning(
                "Document {DocumentId} processing lease {LeaseId} was lost; stopping without writing results.",
                documentId,
                leaseId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReleaseLeaseAsync(documentId, leaseId);
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
            ClearLease(document);
            document.NextProcessingAttemptAt = retryable
                ? DateTime.UtcNow + TimeSpan.FromTicks(
                    _retryDelay.Ticks * document.ProcessingAttemptCount)
                : null;

            try
            {
                await _context.SaveChangesAsync(CancellationToken.None);
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.ChangeTracker.Clear();
                _logger.LogWarning(
                    "Failure state for document {DocumentId} was not written because lease {LeaseId} was lost.",
                    documentId,
                    leaseId);
            }
        }
    }

    private async Task<List<float[]>> GenerateEmbeddingsInBatchesAsync(
        Document document,
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
            await RenewLeaseIfDueAsync(document, cancellationToken);
        }

        return embeddings;
    }

    /// <summary>
    /// Uzun embedding üretimi sırasında worker'ın canlı olduğunu bildirir. Kayıt
    /// çitten geçtiği için sahiplik kaybedildiyse burada fark edilir ve iş durur.
    /// </summary>
    private async Task RenewLeaseIfDueAsync(Document document, CancellationToken cancellationToken)
    {
        var lastHeartbeat = document.ProcessingHeartbeatAt ?? DateTime.MinValue;
        if (DateTime.UtcNow - lastHeartbeat < _heartbeatInterval)
        {
            return;
        }

        await SaveWithHeartbeatAsync(document, cancellationToken);
    }

    private Task<int> SaveWithHeartbeatAsync(Document document, CancellationToken cancellationToken)
    {
        document.ProcessingHeartbeatAt = DateTime.UtcNow;
        return _context.SaveChangesAsync(cancellationToken);
    }

    private async Task ReleaseLeaseAsync(int documentId, Guid leaseId)
    {
        _context.ChangeTracker.Clear();
        using var timeout = new CancellationTokenSource(ReleaseTimeout);
        try
        {
            var released = await DocumentProcessingLeases.ReleaseAsync(
                _context,
                documentId,
                leaseId,
                timeout.Token);
            if (released == 1)
            {
                _logger.LogInformation(
                    "Document {DocumentId} was returned to the queue during shutdown.",
                    documentId);
            }
        }
        catch (Exception exception)
        {
            // Bırakılamazsa sorun değil: lease süre aşımıyla yine devralınır.
            _logger.LogWarning(
                exception,
                "Lease for document {DocumentId} could not be released during shutdown.",
                documentId);
        }
    }

    private static void ClearLease(Document document)
    {
        document.ProcessingLeaseId = null;
        document.ProcessingHeartbeatAt = null;
        document.ProcessingStartedAt = null;
    }

    private static string LimitError(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        return message[..Math.Min(1_000, message.Length)];
    }
}
