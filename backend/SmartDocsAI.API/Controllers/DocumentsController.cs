// Frontend'den gelen belge (PDF) isteklerini karşılar.
// Dosya yükleme, listeleme ve silme işlemlerini yönetir.
// Gerekli işlemler için ilgili Service'leri kullanır.


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
    [Authorize] // Bu controller'daki tüm işlemleri yapmak için geçerli bir JWT Token gereklidir.
    [ApiController]
    [Route("api/[controller]")]
    public class DocumentsController : ControllerBase
    {
        private const long MaxFileSize = 20 * 1024 * 1024;
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IDocumentProcessor _documentProcessor;
        private readonly IOllamaService _ollamaService;
        private readonly IQdrantService _qdrantService;

        public DocumentsController(AppDbContext context, IWebHostEnvironment env, IDocumentProcessor documentProcessor, IOllamaService ollamaService, IQdrantService qdrantService)
        {
            _context = context;
            _env = env;
            _documentProcessor = documentProcessor;
            _ollamaService = ollamaService;
            _qdrantService = qdrantService;
        }

        /// <summary>
        /// Sisteme PDF dosyası yükler ve bilgilerini veritabanına kaydeder.
        /// </summary>
        [HttpPost("upload")]
        public async Task<IActionResult> UploadDocument(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { Message = "Lütfen bir dosya seçin." });
            }

            if (file.Length > MaxFileSize)
            {
                return BadRequest(new { Message = "PDF dosyası en fazla 20 MB olabilir." });
            }

            // Sadece PDF formatına izin veriyoruz.
            var extension = Path.GetExtension(file.FileName).ToLower();
            if (extension != ".pdf")
            {
                return BadRequest(new { Message = "Yalnızca PDF belgeleri yüklenebilir." });
            }


            if (!await HasPdfSignatureAsync(file))
            {
                return BadRequest(new { Message = "Dosya içeriği geçerli bir PDF değil." });
            }

            // JWT Token içerisinden NameIdentifier (Kullanıcı ID) bilgisini çıkarıyoruz.
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
            {
                return Unauthorized(new { Message = "Kullanıcı oturumu geçersiz." });
            }
            int userId = int.Parse(userIdClaim);

            // Sunucuda dosyaların kaydedileceği klasörü belirliyoruz (Uploads/)
            var uploadsFolder = Path.Combine(_env.ContentRootPath, "Uploads");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            // Dosya çakışmalarını önlemek için benzersiz bir dosya adı (GUID ile) üretiyoruz.
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

            // Dosyayı sunucuya kaydediyoruz.
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Veritabanı modelini oluşturup kaydediyoruz.
            var document = new Document
            {
                UserId = userId,
                Title = title,
                FileName = uniqueFileName,
                FileType = extension,
                FilePath = filePath,
                FileSize = file.Length,
                UploadDate = DateTime.UtcNow
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            // PDF dosyasını okuyoruz ve metnini anlamsal parçalara (Chunks) bölüyoruz.
            var chunks = await _documentProcessor.ProcessPdfAsync(document);

            // Bölünen parçaları veritabanımıza (SQL Server) ekliyoruz.
            _context.Chunks.AddRange(chunks);
            await _context.SaveChangesAsync();

            var indexingStatus = "Chunklar veritabanına kaydedildi.";

            if (chunks.Count > 0)
            {
                try
                {
                    var embeddings = await Task.WhenAll(chunks.Select(chunk => _ollamaService.GetEmbeddingAsync(chunk.Content)));
                    await _qdrantService.SaveChunksAsync(chunks, embeddings.ToList());
                    indexingStatus = "Chunklar Qdrant'a kaydedildi.";
                }
                catch
                {
                    indexingStatus = "Belge kaydedildi, ancak vektör indeksleme tamamlanamadı.";
                }
            }
            else
            {
                indexingStatus = "Belgeden işlenecek metin çıkarılamadı.";
            }

            var documentDto = new DocumentDto
            {
                Id = document.Id,
                Title = document.Title,
                FileName = document.FileName,
                FileType = document.FileType,
                FileSize = document.FileSize,
                UploadDate = document.UploadDate
            };

            return Ok(new
            {
                documentDto.Id,
                documentDto.Title,
                documentDto.FileName,
                documentDto.FileType,
                documentDto.FileSize,
                documentDto.UploadDate,
                IndexingStatus = indexingStatus
            });
        }

        /// <summary>
        /// Giriş yapmış olan kullanıcının yüklediği tüm belgeleri listeler.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetDocuments()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
            {
                return Unauthorized();
            }
            int userId = int.Parse(userIdClaim);

            var documents = await _context.Documents
                .Where(d => d.UserId == userId)
                .Select(d => new DocumentDto
                {
                    Id = d.Id,
                    Title = d.Title,
                    FileName = d.FileName,
                    FileType = d.FileType,
                    FileSize = d.FileSize,
                    UploadDate = d.UploadDate
                })
                .ToListAsync();

            return Ok(documents);
        }

        /// <summary>
        /// Belgeyi veritabanından ve sunucudaki fiziksel klasörden siler.
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDocument(int id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
            {
                return Unauthorized();
            }
            int userId = int.Parse(userIdClaim);

            // Silinecek belgeyi ve o belgenin giriş yapan kullanıcıya ait olup olmadığını sorguluyoruz.
            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

            if (document == null)
            {
                return NotFound(new { Message = "Belge bulunamadı veya bu belge üzerinde işlem yapma yetkiniz yok." });
            }

            // Fiziksel dosyayı sunucudan siliyoruz.
            if (System.IO.File.Exists(document.FilePath))
            {
                System.IO.File.Delete(document.FilePath);
            }

            _context.Documents.Remove(document);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Belge başarıyla silindi." });



        }

        private static async Task<bool> HasPdfSignatureAsync(IFormFile file)
        {
            var signature = new byte[5];

            await using var stream = file.OpenReadStream();
            var bytesRead = await stream.ReadAsync(signature.AsMemory());

            return bytesRead == signature.Length
                && signature[0] == 0x25
                && signature[1] == 0x50
                && signature[2] == 0x44
                && signature[3] == 0x46
                && signature[4] == 0x2D;
        }
    }
}
