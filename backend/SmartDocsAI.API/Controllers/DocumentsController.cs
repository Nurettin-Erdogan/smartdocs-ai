using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.DTOs;
using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Models;
using System.Security.Claims;

namespace SmartDocsAI.API.Controllers
{
    // PDF yükleme, listeleme, silme ve yeniden indeksleme işlemlerini yönetir.
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class DocumentsController : ControllerBase
    {
        private const long MaxFileSize = 20 * 1024 * 1024;
        private const int EmbeddingBatchSize = 4;

        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IDocumentProcessor _documentProcessor;
        private readonly IOllamaService _ollamaService;
        private readonly IQdrantService _qdrantService;
        private readonly ILogger<DocumentsController> _logger;

        public DocumentsController(
            AppDbContext context,
            IWebHostEnvironment env,
            IDocumentProcessor documentProcessor,
            IOllamaService ollamaService,
            IQdrantService qdrantService,
            ILogger<DocumentsController> logger)
        {
            _context = context;
            _env = env;
            _documentProcessor = documentProcessor;
            _ollamaService = ollamaService;
            _qdrantService = qdrantService;
            _logger = logger;
        }

        /// <summary>
        /// PDF dosyasını kaydeder, metnini parçalara böler ve Qdrant'a indeksler.
        /// </summary>
        [HttpPost("upload")]
        public async Task<IActionResult> UploadDocument(IFormFile file)
        {
            // Dosyanın temel kurallara uygunluğunu kontrol eder.
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { Message = "Lütfen bir dosya seçin." });
            }

            if (file.Length > MaxFileSize)
            {
                return BadRequest(new { Message = "PDF dosyası en fazla 20 MB olabilir." });
            }

            var extension = Path.GetExtension(file.FileName).ToLower();
            if (extension != ".pdf")
            {
                return BadRequest(new { Message = "Yalnızca PDF belgeleri yüklenebilir." });
            }

            if (!await HasPdfSignatureAsync(file))
            {
                return BadRequest(new { Message = "Dosya içeriği geçerli bir PDF değil." });
            }

            // Belgeyi giriş yapan kullanıcıyla ilişkilendirmek için JWT'den kullanıcı ID'sini alır.
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
            {
                return Unauthorized(new { Message = "Kullanıcı oturumu geçersiz." });
            }

            if (!int.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { Message = "Kullanıcı oturumu geçersiz." });
            }

            // Dosya için güvenli ve benzersiz bir kayıt yolu oluşturur.
            var uploadsFolder = Path.Combine(_env.ContentRootPath, "Uploads");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var safeOriginalFileName = Path.GetFileName(file.FileName);
            var uniqueFileName = $"{Guid.NewGuid():N}.pdf";
            var title = Path.GetFileNameWithoutExtension(safeOriginalFileName).Trim();

            if (string.IsNullOrWhiteSpace(title))
            {
                return BadRequest(new { Message = "PDF dosyasının geçerli bir adı olmalıdır." });
            }

            if (title.Length > 255)
            {
                title = title[..255];
            }

            var filePath = Path.Combine(uploadsFolder, uniqueFileName);
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var uploadCommitted = false;

            try
            {
                // PDF'yi sunucuya kaydeder.
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // Belge kaydını PostgreSQL'e ekler.
                var document = new Document
                {
                    UserId = userId,
                    Title = title,
                    FileName = uniqueFileName,
                    FileType = extension,
                    FilePath = filePath,
                    FileSize = file.Length,
                    UploadDate = DateTime.UtcNow,
                    IndexingStatus = "Pending"
                };

                _context.Documents.Add(document);
                await _context.SaveChangesAsync();

                // PDF metnini chunk'lara böler ve PostgreSQL'e kaydeder.
                var chunks = await _documentProcessor.ProcessPdfAsync(document);
                _context.Chunks.AddRange(chunks);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                uploadCommitted = true;

                // Chunk'ların embedding'lerini üretip Qdrant'a kaydeder.
                if (chunks.Count > 0)
                {
                    try
                    {
                        var embeddings = await GenerateEmbeddingsInBatchesAsync(chunks);
                        await _qdrantService.SaveChunksAsync(chunks, embeddings);
                        document.IndexingStatus = "Ready";
                        document.IndexingError = null;
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(exception, "Belge {DocumentId} Qdrant'a indekslenemedi.", document.Id);
                        document.IndexingStatus = "Failed";
                        document.IndexingError = exception.GetBaseException().Message[..Math.Min(1000, exception.GetBaseException().Message.Length)];
                    }
                }
                else
                {
                    document.IndexingStatus = "NoContent";
                    document.IndexingError = "Belgeden işlenecek metin çıkarılamadı.";
                }

                await _context.SaveChangesAsync();

                var documentDto = new DocumentDto
                {
                    Id = document.Id,
                    Title = document.Title,
                    FileName = document.FileName,
                    FileType = document.FileType,
                    FileSize = document.FileSize,
                    UploadDate = document.UploadDate,
                    IndexingStatus = document.IndexingStatus
                };

                return Ok(new
                {
                    documentDto.Id,
                    documentDto.Title,
                    documentDto.FileName,
                    documentDto.FileType,
                    documentDto.FileSize,
                    documentDto.UploadDate,
                    IndexingStatus = document.IndexingStatus
                });
            }
            finally
            {
                // Veritabanı işlemi tamamlanmadıysa yarım kalan fiziksel dosyayı temizler.
                if (!uploadCommitted && System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }
        }

        /// <summary>
        /// Giriş yapan kullanıcının belgelerini listeler.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetDocuments()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
            {
                return Unauthorized();
            }

            if (!int.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { Message = "Kullanıcı oturumu geçersiz." });
            }

            // Yalnızca giriş yapan kullanıcıya ait belgeleri getirir.
            var documents = await _context.Documents
                .Where(d => d.UserId == userId)
                .OrderByDescending(d => d.UploadDate)
                .Select(d => new DocumentDto
                {
                    Id = d.Id,
                    Title = d.Title,
                    FileName = d.FileName,
                    FileType = d.FileType,
                    FileSize = d.FileSize,
                    UploadDate = d.UploadDate,
                    IndexingStatus = d.IndexingStatus
                })
                .ToListAsync();

            return Ok(documents);
        }

        /// <summary>
        /// Belgeyi Qdrant'tan, sunucudan ve PostgreSQL'den siler.
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDocument(int id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
            {
                return Unauthorized();
            }

            if (!int.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { Message = "Kullanıcı oturumu geçersiz." });
            }

            // Belgenin kullanıcıya ait olduğunu da kontrol eder.
            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

            if (document == null)
            {
                return NotFound(new { Message = "Belge bulunamadı veya bu belge üzerinde işlem yapma yetkiniz yok." });
            }

            try
            {
                await _qdrantService.DeleteDocumentChunksAsync(document.Id);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException)
            {
                if (document.IndexingStatus == "Ready")
                {
                    _logger.LogError(exception, "Belge {DocumentId} için Qdrant temizliği başarısız oldu.", document.Id);
                    return StatusCode(
                        StatusCodes.Status503ServiceUnavailable,
                        new { Message = "Belge şu anda silinemiyor. Qdrant servisine ulaşılamadı." });
                }

                _logger.LogWarning(
                    exception,
                    "İndekslenmemiş belge {DocumentId}, Qdrant kapalı olmasına rağmen yerel kayıtlardan siliniyor.",
                    document.Id);
            }

            if (System.IO.File.Exists(document.FilePath))
            {
                System.IO.File.Delete(document.FilePath);
            }

            _context.Documents.Remove(document);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Belge başarıyla silindi." });
        }

        /// <summary>
        /// Başarısız olan belge indeksleme işlemini yeniden dener.
        /// </summary>
        [HttpPost("{id}/reindex")]
        public async Task<IActionResult> ReindexDocument(int id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { Message = "Kullanıcı oturumu geçersiz." });
            }

            var document = await _context.Documents
                .Include(d => d.Chunks)
                .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

            if (document == null)
            {
                return NotFound(new { Message = "Belge bulunamadı veya bu belge üzerinde işlem yapma yetkiniz yok." });
            }

            var chunks = document.Chunks
                .OrderBy(chunk => chunk.ChunkIndex)
                .ToList();

            if (chunks.Count == 0)
            {
                document.IndexingStatus = "NoContent";
                document.IndexingError = "Belgeden işlenecek metin çıkarılamadı.";
                await _context.SaveChangesAsync();

                return BadRequest(new { Message = document.IndexingError });
            }

            document.IndexingStatus = "Pending";
            document.IndexingError = null;
            await _context.SaveChangesAsync();

            try
            {
                // Eski vektörleri temizleyip embedding'leri yeniden oluşturur.
                await _qdrantService.DeleteDocumentChunksAsync(document.Id);
                var embeddings = await GenerateEmbeddingsInBatchesAsync(chunks);
                await _qdrantService.SaveChunksAsync(chunks, embeddings);

                document.IndexingStatus = "Ready";
                document.IndexingError = null;
                await _context.SaveChangesAsync();

                return Ok(ToDocumentDto(document));
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Belge {DocumentId} yeniden indekslenemedi.", document.Id);
                document.IndexingStatus = "Failed";
                document.IndexingError = LimitIndexingError(exception);
                await _context.SaveChangesAsync();

                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new { Message = "Belge indekslenemedi. Ollama ve Qdrant servislerini kontrol edin." });
            }
        }

        // Chunk embedding'lerini sunucuyu yormamak için küçük gruplar hâlinde üretir.
        private async Task<List<float[]>> GenerateEmbeddingsInBatchesAsync(List<Chunk> chunks)
        {
            var embeddings = new List<float[]>(chunks.Count);

            foreach (var batch in chunks.Chunk(EmbeddingBatchSize))
            {
                var batchEmbeddings = await Task.WhenAll(
                    batch.Select(chunk => _ollamaService.GetEmbeddingAsync(chunk.Content)));

                embeddings.AddRange(batchEmbeddings);
            }

            return embeddings;
        }

        private static DocumentDto ToDocumentDto(Document document)
        {
            return new DocumentDto
            {
                Id = document.Id,
                Title = document.Title,
                FileName = document.FileName,
                FileType = document.FileType,
                FileSize = document.FileSize,
                UploadDate = document.UploadDate,
                IndexingStatus = document.IndexingStatus
            };
        }

        // Veritabanında tutulacak hata mesajını en fazla 1000 karaktere indirir.
        private static string LimitIndexingError(Exception exception)
        {
            var message = exception.GetBaseException().Message;
            return message[..Math.Min(1000, message.Length)];
        }

        // Dosyanın "%PDF-" imzasıyla başlayıp başlamadığını kontrol eder.
        private static async Task<bool> HasPdfSignatureAsync(IFormFile file)
        {
            var signature = new byte[5];

            await using var stream = file.OpenReadStream();
            var bytesRead = await stream.ReadAtLeastAsync(
                signature,
                signature.Length,
                throwOnEndOfStream: false);

            return bytesRead == signature.Length
                && signature[0] == 0x25
                && signature[1] == 0x50
                && signature[2] == 0x44
                && signature[3] == 0x46
                && signature[4] == 0x2D;
        }
    }
}
