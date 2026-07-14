using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.DTOs;
using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Models;

namespace SmartDocsAI.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public sealed class DocumentsController : ControllerBase
{
    private const long MaxFileSize = 20 * 1024 * 1024;
    private const long MaxRequestSize = MaxFileSize + (512 * 1024);
    private const int EmbeddingBatchSize = 4;
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly IDocumentProcessor _documentProcessor;
    private readonly IOllamaService _ollamaService;
    private readonly IQdrantService _qdrantService;
    private readonly IDocumentDeletionService _documentDeletionService;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        AppDbContext context,
        IWebHostEnvironment environment,
        IDocumentProcessor documentProcessor,
        IOllamaService ollamaService,
        IQdrantService qdrantService,
        IDocumentDeletionService documentDeletionService,
        ILogger<DocumentsController> logger)
    {
        _context = context;
        _environment = environment;
        _documentProcessor = documentProcessor;
        _ollamaService = ollamaService;
        _qdrantService = qdrantService;
        _documentDeletionService = documentDeletionService;
        _logger = logger;
    }

    [HttpPost("upload")]
    [EnableRateLimiting("DocumentWritePolicy")]
    [RequestSizeLimit(MaxRequestSize)]
    public async Task<IActionResult> UploadDocument(
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { Message = "Lütfen bir dosya seçin." });
        }

        if (file.Length > MaxFileSize)
        {
            return BadRequest(new { Message = "PDF dosyası en fazla 20 MB olabilir." });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension != ".pdf")
        {
            return BadRequest(new { Message = "Yalnızca PDF belgeleri yüklenebilir." });
        }

        if (!await HasPdfSignatureAsync(file, cancellationToken))
        {
            return BadRequest(new { Message = "Dosya içeriği geçerli bir PDF değil." });
        }

        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Kullanıcı oturumu geçersiz." });
        }

        var safeOriginalFileName = new string(Path.GetFileName(file.FileName)
            .Where(character => !char.IsControl(character))
            .ToArray())
            .Trim();
        var title = Path.GetFileNameWithoutExtension(safeOriginalFileName).Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return BadRequest(new { Message = "PDF dosyasının geçerli bir adı olmalıdır." });
        }

        title = title[..Math.Min(title.Length, 255)];
        safeOriginalFileName = safeOriginalFileName.Length <= 255
            ? safeOriginalFileName
            : $"{Path.GetFileNameWithoutExtension(safeOriginalFileName)[..250]}.pdf";

        var uploadsFolder = Path.Combine(_environment.ContentRootPath, "Uploads");
        Directory.CreateDirectory(uploadsFolder);

        var uniqueFileName = $"{Guid.NewGuid():N}.pdf";
        var filePath = Path.Combine(uploadsFolder, uniqueFileName);
        var uploadCommitted = false;
        Document? document = null;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await using (var stream = new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81_920,
                useAsync: true))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            document = new Document
            {
                UserId = userId,
                Title = title,
                FileName = safeOriginalFileName,
                FileType = extension,
                FilePath = filePath,
                FileSize = file.Length,
                UploadDate = DateTime.UtcNow,
                IndexingStatus = "Pending"
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync(cancellationToken);

            var chunks = await _documentProcessor.ProcessPdfAsync(document, cancellationToken);
            _context.Chunks.AddRange(chunks);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            uploadCommitted = true;

            try
            {
                await IndexDocumentAsync(document, chunks, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Document {DocumentId} could not be indexed.", document.Id);
                document.IndexingStatus = "Failed";
                document.IndexingError = LimitIndexingError(exception);
            }

            await _context.SaveChangesAsync(cancellationToken);

            return Ok(ToDocumentDto(document));
        }
        catch (InvalidDataException exception) when (!uploadCommitted)
        {
            return BadRequest(new { Message = exception.Message });
        }
        catch (OperationCanceledException) when (document is not null && uploadCommitted)
        {
            document.IndexingStatus = "Failed";
            document.IndexingError = "İndeksleme isteği iptal edildi.";
            await TrySaveIndexingStateAsync();
            throw;
        }
        finally
        {
            if (!uploadCommitted && System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetDocuments(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Kullanıcı oturumu geçersiz." });
        }

        var documents = await _context.Documents
            .AsNoTracking()
            .Where(document => document.UserId == userId)
            .OrderByDescending(document => document.UploadDate)
            .Select(document => new DocumentDto
            {
                Id = document.Id,
                Title = document.Title,
                FileName = document.FileName,
                FileType = document.FileType,
                FileSize = document.FileSize,
                UploadDate = document.UploadDate,
                IndexingStatus = document.IndexingStatus
            })
            .ToListAsync(cancellationToken);

        return Ok(documents);
    }

    [HttpDelete("{id:int}")]
    [EnableRateLimiting("DocumentWritePolicy")]
    public async Task<IActionResult> DeleteDocument(int id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Kullanıcı oturumu geçersiz." });
        }

        var document = await _context.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == id && item.UserId == userId,
                cancellationToken);

        if (document is null)
        {
            return NotFound(new { Message = "Belge bulunamadı veya bu belge üzerinde işlem yapma yetkiniz yok." });
        }

        if (document.IndexingStatus == "Pending")
        {
            return Conflict(new { Message = "İndeksleme sürerken belge silinemez." });
        }

        if (document.IndexingStatus != "Deleting")
        {
            var transitioned = await _context.Documents
                .Where(item =>
                    item.Id == document.Id &&
                    item.UserId == userId &&
                    item.IndexingStatus == document.IndexingStatus)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(item => item.IndexingStatus, "Deleting")
                        .SetProperty(item => item.IndexingError, (string?)null),
                    cancellationToken);

            if (transitioned == 0)
            {
                return Conflict(new { Message = "Belge üzerinde başka bir işlem devam ediyor." });
            }
        }

        try
        {
            await _documentDeletionService.DeleteAsync(document.Id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Document {DocumentId} deletion was queued for retry.",
                document.Id);
            var error = LimitIndexingError(exception);
            await _context.Documents
                .Where(item => item.Id == document.Id && item.IndexingStatus == "Deleting")
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(item => item.IndexingError, error),
                    CancellationToken.None);

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { Message = "Belge silme işlemi kuyruğa alındı ve otomatik olarak tekrar denenecek." });
        }

        return Ok(new { Message = "Belge başarıyla silindi." });
    }

    [HttpPost("{id:int}/reindex")]
    [EnableRateLimiting("DocumentWritePolicy")]
    public async Task<IActionResult> ReindexDocument(int id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new { Message = "Kullanıcı oturumu geçersiz." });
        }

        var documentSnapshot = await _context.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == id && item.UserId == userId,
                cancellationToken);

        if (documentSnapshot is null)
        {
            return NotFound(new { Message = "Belge bulunamadı veya bu belge üzerinde işlem yapma yetkiniz yok." });
        }

        if (documentSnapshot.IndexingStatus is "Pending" or "Deleting")
        {
            return Conflict(new { Message = "Bu belge üzerinde başka bir işlem devam ediyor." });
        }

        var previousStatus = documentSnapshot.IndexingStatus;
        var transitioned = await _context.Documents
            .Where(item =>
                item.Id == id &&
                item.UserId == userId &&
                item.IndexingStatus == previousStatus)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.IndexingStatus, "Pending")
                    .SetProperty(item => item.IndexingError, (string?)null),
                cancellationToken);

        if (transitioned == 0)
        {
            return Conflict(new { Message = "Bu belge üzerinde başka bir işlem devam ediyor." });
        }

        var document = await _context.Documents
            .Include(item => item.Chunks)
            .FirstOrDefaultAsync(
                item => item.Id == id && item.UserId == userId,
                cancellationToken);

        if (document is null)
        {
            return NotFound(new { Message = "Belge bulunamadı." });
        }

        var chunks = document.Chunks.OrderBy(chunk => chunk.ChunkIndex).ToList();
        if (chunks.Count == 0)
        {
            document.IndexingStatus = "NoContent";
            document.IndexingError = "Belgeden işlenecek metin çıkarılamadı.";
            await _context.SaveChangesAsync(cancellationToken);
            return BadRequest(new { Message = document.IndexingError });
        }

        try
        {
            await IndexDocumentAsync(document, chunks, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return Ok(ToDocumentDto(document));
        }
        catch (OperationCanceledException)
        {
            document.IndexingStatus = previousStatus == "Ready" ? "Ready" : "Failed";
            document.IndexingError = "Yeniden indeksleme isteği iptal edildi; önceki indeks korundu.";
            await TrySaveIndexingStateAsync();
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Document {DocumentId} could not be reindexed.", document.Id);
            document.IndexingStatus = previousStatus == "Ready" ? "Ready" : "Failed";
            document.IndexingError = LimitIndexingError(exception);
            await _context.SaveChangesAsync(CancellationToken.None);

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    Message = previousStatus == "Ready"
                        ? "Yeniden indeksleme başarısız oldu; çalışan önceki indeks korundu."
                        : "Belge indekslenemedi. Ollama ve Qdrant servislerini kontrol edin."
                });
        }
    }

    private async Task IndexDocumentAsync(
        Document document,
        List<Chunk> chunks,
        CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
        {
            document.IndexingStatus = "NoContent";
            document.IndexingError = "Belgeden işlenecek metin çıkarılamadı.";
            return;
        }

        var embeddings = await GenerateEmbeddingsInBatchesAsync(chunks, cancellationToken);
        var indexVersion = Guid.NewGuid().ToString("N");

        // New vectors are fully written before older versions are removed.
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
        catch (Exception cleanupException) when (cleanupException is not OperationCanceledException)
        {
            // The fresh version is usable. Duplicate old points are deduplicated during search.
            _logger.LogWarning(
                cleanupException,
                "Old Qdrant vectors for document {DocumentId} could not be cleaned up.",
                document.Id);
        }

        document.IndexingStatus = "Ready";
        document.IndexingError = null;
        document.CurrentIndexVersion = indexVersion;
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

    private bool TryGetUserId(out int userId)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out userId);
    }

    private async Task TrySaveIndexingStateAsync()
    {
        try
        {
            await _context.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The canceled indexing state could not be persisted.");
        }
    }

    private static DocumentDto ToDocumentDto(Document document) => new()
    {
        Id = document.Id,
        Title = document.Title,
        FileName = document.FileName,
        FileType = document.FileType,
        FileSize = document.FileSize,
        UploadDate = document.UploadDate,
        IndexingStatus = document.IndexingStatus
    };

    private static string LimitIndexingError(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        return message[..Math.Min(1_000, message.Length)];
    }

    private static async Task<bool> HasPdfSignatureAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var signature = new byte[5];
        await using var stream = file.OpenReadStream();
        var bytesRead = await stream.ReadAtLeastAsync(
            signature,
            signature.Length,
            throwOnEndOfStream: false,
            cancellationToken);

        return bytesRead == signature.Length
            && signature[0] == 0x25
            && signature[1] == 0x50
            && signature[2] == 0x44
            && signature[3] == 0x46
            && signature[4] == 0x2D;
    }
}
