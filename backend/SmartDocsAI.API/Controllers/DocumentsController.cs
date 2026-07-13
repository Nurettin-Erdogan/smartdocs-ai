// Kullanıcının JWT tokenını kontrol etmemizi sağlar.
// [Authorize] özelliği bu kütüphaneden gelir.
using Microsoft.AspNetCore.Authorization;

// Controller, endpoint ve HTTP cevaplarını kullanmamızı sağlar.
// Ok(), BadRequest(), NotFound() ve Unauthorized() buradan gelir.
using Microsoft.AspNetCore.Mvc;

// Entity Framework Core ile PostgreSQL sorguları yapmamızı sağlar.
// Include(), Where(), FirstOrDefaultAsync() ve ToListAsync() buradan gelir.
using Microsoft.EntityFrameworkCore;

// Projedeki AppDbContext sınıfını kullanmamızı sağlar.
// Belge ve chunk bilgileri veritabanına bununla kaydedilir.
using SmartDocsAI.API.Data;

// Frontend’e gönderilecek belge verilerinin şeklini belirleyen DTO’ları kullanır.
// Bu controller içinde DocumentDto kullanılmaktadır.
using SmartDocsAI.API.DTOs;

// PDF işleme, Ollama ve Qdrant servislerinin interface’lerini kullanmamızı sağlar.
using SmartDocsAI.API.Interfaces;

// Document ve Chunk gibi veritabanı modellerini kullanmamızı sağlar.
using SmartDocsAI.API.Models;

// JWT token içindeki kullanıcı kimliğini okumamızı sağlar.
// ClaimTypes.NameIdentifier kullanıcının ID bilgisini temsil eder.
using System.Security.Claims;


// Bu sınıfın Controllers bölümüne ait olduğunu belirtir.
namespace SmartDocsAI.API.Controllers
{
    // Bu controller içindeki bütün işlemler için giriş yapılmasını zorunlu tutar.
    // Geçerli JWT tokenı olmayan kişiler endpointleri kullanamaz.
    [Authorize]

    // Bu sınıfın bir Web API controller’ı olduğunu belirtir.
    [ApiController]

    // Controller’ın temel adresini belirler.
    // DocumentsController isminden dolayı adres /api/documents olur.
    [Route("api/[controller]")]

    // PDF yükleme, listeleme, silme ve yeniden indeksleme işlemlerini yönetir.
    public class DocumentsController : ControllerBase
    {
        // Yüklenebilecek PDF dosyasının en büyük boyutunu belirler.
        // 20 * 1024 * 1024 işlemi 20 megabayta karşılık gelir.
        private const long MaxFileSize = 20 * 1024 * 1024;

        // Embedding oluşturulurken metin parçalarının kaçar kaçar işleneceğini belirler.
        // Burada chunk’lar dörderli gruplar hâlinde Ollama’ya gönderilir.
        private const int EmbeddingBatchSize = 4;


        // PostgreSQL veritabanıyla iletişim kurmak için kullanılır.
        private readonly AppDbContext _context;

        // Projenin sunucuda çalıştığı ana klasörün yolunu bulur.
        // Uploads klasörünün yerini belirlemek için kullanılır.
        private readonly IWebHostEnvironment _env;

        // PDF içindeki metni çıkarmak ve metni chunk’lara bölmek için kullanılır.
        private readonly IDocumentProcessor _documentProcessor;

        // Metinlerin embedding değerlerini oluşturmak için Ollama’yı kullanır.
        private readonly IOllamaService _ollamaService;

        // Chunk ve embedding bilgilerini Qdrant’a kaydetmek veya silmek için kullanılır.
        private readonly IQdrantService _qdrantService;

        // Oluşan hata ve uyarıları kayıt altına almak için kullanılır.
        private readonly ILogger<DocumentsController> _logger;


        // DocumentsController oluşturulurken ihtiyaç duyduğu servisleri dışarıdan alır.
        // Bu sisteme Dependency Injection denir.
        public DocumentsController(
            // PostgreSQL veritabanıyla çalışacak nesnedir.
            AppDbContext context,

            // Sunucu klasörlerinin konumunu verecek nesnedir.
            IWebHostEnvironment env,

            // PDF’yi okuyup chunk’lara bölecek servistir.
            IDocumentProcessor documentProcessor,

            // Metinlerin embedding’lerini oluşturacak servistir.
            IOllamaService ollamaService,

            // Embedding’leri Qdrant’a kaydedecek servistir.
            IQdrantService qdrantService,

            // Hata ve uyarıları kaydedecek log servisidir.
            ILogger<DocumentsController> logger)
        {
            // Dışarıdan gelen veritabanı nesnesini sınıf içinde saklar.
            _context = context;

            // Dışarıdan gelen sunucu ortamı bilgisini sınıf içinde saklar.
            _env = env;

            // Dışarıdan gelen PDF işleme servisini sınıf içinde saklar.
            _documentProcessor = documentProcessor;

            // Dışarıdan gelen Ollama servisini sınıf içinde saklar.
            _ollamaService = ollamaService;

            // Dışarıdan gelen Qdrant servisini sınıf içinde saklar.
            _qdrantService = qdrantService;

            // Dışarıdan gelen log servisini sınıf içinde saklar.
            _logger = logger;
        }


        /// <summary>
        /// Sisteme PDF dosyası yükler ve bilgilerini veritabanına kaydeder.
        /// </summary>

        // Bu metodun POST isteğiyle çalışacağını belirtir.
        // Tam adres POST /api/documents/upload olur.
        [HttpPost("upload")]

        // Frontend’den gönderilen dosyayı IFormFile türünde alır.
        // async olduğu için uzun işlemler sırasında uygulamayı gereksiz yere bekletmez.
        public async Task<IActionResult> UploadDocument(IFormFile file)
        {
            // Dosya gönderilmemişse veya dosyanın boyutu sıfırsa bu blok çalışır.
            if (file == null || file.Length == 0)
            {
                // Frontend’e 400 Bad Request cevabı gönderir.
                return BadRequest(new
                {
                    Message = "Lütfen bir dosya seçin."
                });
            }


            // Dosya boyutunun belirlenen 20 MB sınırından büyük olup olmadığını kontrol eder.
            if (file.Length > MaxFileSize)
            {
                // Dosya çok büyükse yükleme işlemini durdurur.
                return BadRequest(new
                {
                    Message = "PDF dosyası en fazla 20 MB olabilir."
                });
            }


            // Dosyanın uzantısını alır ve küçük harfe çevirir.
            // Örneğin ".PDF" uzantısını ".pdf" hâline getirir.
            var extension = Path
                .GetExtension(file.FileName)
                .ToLower();

            // Dosya uzantısının .pdf olup olmadığını kontrol eder.
            if (extension != ".pdf")
            {
                // PDF dışında bir dosya gönderilmişse işlemi durdurur.
                return BadRequest(new
                {
                    Message = "Yalnızca PDF belgeleri yüklenebilir."
                });
            }


            // Dosyanın yalnızca adının değil, içeriğinin de PDF olup olmadığını kontrol eder.
            // Böylece başka bir dosyanın uzantısını .pdf yapmak yeterli olmaz.
            if (!await HasPdfSignatureAsync(file))
            {
                // Dosyanın başlangıç bilgileri PDF formatına uygun değilse hata döndürür.
                return BadRequest(new
                {
                    Message = "Dosya içeriği geçerli bir PDF değil."
                });
            }


            // JWT token içerisindeki kullanıcı ID bilgisini bulur.
            // NameIdentifier değeri TokenService tarafından token içine eklenmiştir.
            var userIdClaim = User
                .FindFirst(ClaimTypes.NameIdentifier)?
                .Value;

            // Token içinde kullanıcı ID bilgisi yoksa oturum geçersiz kabul edilir.
            if (string.IsNullOrEmpty(userIdClaim))
            {
                // 401 Unauthorized cevabı döndürür.
                return Unauthorized(new
                {
                    Message = "Kullanıcı oturumu geçersiz."
                });
            }


            // Token içindeki kullanıcı ID’si yazı olarak gelir.
            // TryParse bu yazıyı int türündeki bir sayıya çevirmeyi dener.
            if (!int.TryParse(userIdClaim, out var userId))
            {
                // Kullanıcı ID’si sayıya çevrilemiyorsa token geçersiz kabul edilir.
                return Unauthorized(new
                {
                    Message = "Kullanıcı oturumu geçersiz."
                });
            }


            // Projenin ana klasör yoluyla Uploads klasörünü birleştirir.
            // PDF dosyaları bu klasörün içinde tutulacaktır.
            var uploadsFolder = Path.Combine(
                _env.ContentRootPath,
                "Uploads");

            // Uploads klasörü daha önce oluşturulmamışsa bu blok çalışır.
            if (!Directory.Exists(uploadsFolder))
            {
                // Sunucuda Uploads isimli klasörü oluşturur.
                Directory.CreateDirectory(uploadsFolder);
            }


            // Kullanıcının gönderdiği dosya isminden klasör bilgilerini temizler.
            // Böylece dosya yolu üzerinden yapılabilecek saldırılar engellenmeye çalışılır.
            var safeOriginalFileName = Path.GetFileName(file.FileName);

            // Sunucuda kullanılmak üzere benzersiz bir dosya adı üretir.
            // GUID sayesinde aynı isimli iki PDF birbirinin üzerine yazılmaz.
            var uniqueFileName = $"{Guid.NewGuid():N}.pdf";


            // Dosyanın uzantısız orijinal adını belge başlığı olarak alır.
            // Başındaki ve sonundaki boşlukları temizler.
            var title = Path
                .GetFileNameWithoutExtension(safeOriginalFileName)
                .Trim();


            // Dosya adı boşsa veya yalnızca boşluklardan oluşuyorsa işlem durdurulur.
            if (string.IsNullOrWhiteSpace(title))
            {
                return BadRequest(new
                {
                    Message = "PDF dosyasının geçerli bir adı olmalıdır."
                });
            }


            // Belge başlığı 255 karakterden uzunsa bu blok çalışır.
            if (title.Length > 255)
            {
                // Başlığın yalnızca ilk 255 karakterini alır.
                // Böylece veritabanındaki alan sınırı aşılmaz.
                title = title[..255];
            }


            // Uploads klasörüyle benzersiz dosya adını birleştirir.
            // Sonuç, PDF’nin sunucuda kaydedileceği tam dosya yoludur.
            var filePath = Path.Combine(
                uploadsFolder,
                uniqueFileName);


            // Veritabanında transaction başlatır.
            // Bir işlem başarısız olursa yarım kalmış verilerin kaydedilmesini önler.
            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            // Veritabanı işleminin başarıyla tamamlanıp tamamlanmadığını tutar.
            // Başlangıçta henüz tamamlanmadığı için false değerindedir.
            var uploadCommitted = false;


            // Dosya ve veritabanı işlemlerinin yapılacağı güvenli bloktur.
            try
            {
                // Dosyayı belirtilen konumda oluşturur.
                // using sayesinde işlem bitince dosya bağlantısı otomatik kapatılır.
                using (var stream = new FileStream(
                    filePath,
                    FileMode.Create))
                {
                    // Frontend’den gelen PDF verisini sunucudaki dosyaya kopyalar.
                    await file.CopyToAsync(stream);
                }


                // PostgreSQL’e kaydedilecek yeni bir Document nesnesi oluşturur.
                var document = new Document
                {
                    // Belgenin hangi kullanıcıya ait olduğunu kaydeder.
                    UserId = userId,

                    // Dosyanın orijinal isminden oluşturulan başlığı kaydeder.
                    Title = title,

                    // Sunucuda kullanılan benzersiz dosya adını kaydeder.
                    FileName = uniqueFileName,

                    // Dosyanın türünü, yani .pdf bilgisini kaydeder.
                    FileType = extension,

                    // Dosyanın sunucudaki tam konumunu kaydeder.
                    FilePath = filePath,

                    // Dosyanın byte cinsinden boyutunu kaydeder.
                    FileSize = file.Length,

                    // Dosyanın yüklenme tarihini UTC saatine göre kaydeder.
                    UploadDate = DateTime.UtcNow,

                    // Belgenin henüz Qdrant’a hazır olmadığını belirtir.
                    IndexingStatus = "Pending"
                };


                // Yeni document nesnesini veritabanına eklenecekler listesine koyar.
                _context.Documents.Add(document);

                // Belge bilgilerini PostgreSQL’e kaydeder.
                // Bu işlemden sonra belgeye otomatik bir ID verilir.
                await _context.SaveChangesAsync();


                // Kaydedilen PDF’yi okur ve metnini küçük parçalara böler.
                // Dönen chunks listesinde belgeden çıkarılan metin parçaları bulunur.
                var chunks =
                    await _documentProcessor.ProcessPdfAsync(document);


                // Oluşturulan bütün chunk nesnelerini PostgreSQL’e ekler.
                _context.Chunks.AddRange(chunks);

                // Chunk bilgilerini veritabanına kaydeder.
                await _context.SaveChangesAsync();

                // Transaction içindeki belge ve chunk kayıtlarını kesinleştirir.
                await transaction.CommitAsync();

                // Veritabanı işleminin başarıyla tamamlandığını belirtir.
                uploadCommitted = true;


                // PDF’den en az bir metin parçası çıkarılmışsa bu blok çalışır.
                if (chunks.Count > 0)
                {
                    // Ollama ve Qdrant işlemlerinde hata oluşabileceği için try kullanılır.
                    try
                    {
                        // Chunk’ların embedding değerlerini dörderli gruplarla oluşturur.
                        var embeddings =
                            await GenerateEmbeddingsInBatchesAsync(chunks);

                        // Chunk’ları ve karşılık gelen embedding değerlerini Qdrant’a kaydeder.
                        await _qdrantService.SaveChunksAsync(
                            chunks,
                            embeddings);

                        // Qdrant kaydı başarılı olduğu için belgeyi hazır olarak işaretler.
                        document.IndexingStatus = "Ready";

                        // Daha önce oluşmuş bir indeksleme hatası varsa temizler.
                        document.IndexingError = null;
                    }
                    // Ollama veya Qdrant işleminde bir hata oluşursa bu blok çalışır.
                    catch (Exception exception)
                    {
                        // Oluşan hatayı belge ID’siyle birlikte loglara yazar.
                        _logger.LogError(
                            exception,
                            "Belge {DocumentId} Qdrant'a indekslenemedi.",
                            document.Id);

                        // Belgenin indeksleme durumunu başarısız olarak değiştirir.
                        document.IndexingStatus = "Failed";

                        // Hatanın en temel mesajını alır.
                        // Mesaj çok uzunsa ilk 1000 karakterini saklar.
                        document.IndexingError =
                            exception
                                .GetBaseException()
                                .Message[
                                    ..Math.Min(
                                        1000,
                                        exception
                                            .GetBaseException()
                                            .Message.Length)];
                    }
                }
                // PDF’den hiçbir metin çıkarılamadıysa bu blok çalışır.
                else
                {
                    // Belgenin işlenecek içeriği olmadığını belirtir.
                    document.IndexingStatus = "NoContent";

                    // Neden indekslenemediğini açıklayan mesajı saklar.
                    document.IndexingError =
                        "Belgeden işlenecek metin çıkarılamadı.";
                }


                // Belgenin Ready, Failed veya NoContent durumunu PostgreSQL’e kaydeder.
                await _context.SaveChangesAsync();


                // Frontend’e gönderilecek temiz bir DocumentDto nesnesi oluşturur.
                var documentDto = new DocumentDto
                {
                    // Belgenin veritabanındaki ID’sini aktarır.
                    Id = document.Id,

                    // Belgenin başlığını aktarır.
                    Title = document.Title,

                    // Belgenin sunucudaki dosya adını aktarır.
                    FileName = document.FileName,

                    // Belgenin dosya türünü aktarır.
                    FileType = document.FileType,

                    // Belgenin dosya boyutunu aktarır.
                    FileSize = document.FileSize,

                    // Belgenin yüklenme tarihini aktarır.
                    UploadDate = document.UploadDate,

                    // Belgenin indeksleme durumunu aktarır.
                    IndexingStatus = document.IndexingStatus
                };


                // Yükleme işlemi sonucunu frontend’e 200 OK olarak gönderir.
                return Ok(new
                {
                    // DocumentDto içindeki belge ID’sini gönderir.
                    documentDto.Id,

                    // DocumentDto içindeki belge başlığını gönderir.
                    documentDto.Title,

                    // DocumentDto içindeki dosya adını gönderir.
                    documentDto.FileName,

                    // DocumentDto içindeki dosya türünü gönderir.
                    documentDto.FileType,

                    // DocumentDto içindeki dosya boyutunu gönderir.
                    documentDto.FileSize,

                    // DocumentDto içindeki yüklenme tarihini gönderir.
                    documentDto.UploadDate,

                    // Belgenin Qdrant indeksleme durumunu gönderir.
                    IndexingStatus = document.IndexingStatus
                });
            }
            // Try içindeki işlem başarılı olsa da hata verse de finally her zaman çalışır.
            finally
            {
                // Veritabanı kaydı tamamlanmadıysa ve fiziksel PDF oluşturulduysa çalışır.
                if (!uploadCommitted &&
                    System.IO.File.Exists(filePath))
                {
                    // Yarım kalan yükleme işlemine ait PDF’yi sunucudan siler.
                    System.IO.File.Delete(filePath);
                }
            }
        }


        /// <summary>
        /// Giriş yapmış olan kullanıcının yüklediği tüm belgeleri listeler.
        /// </summary>

        // Bu metodun GET isteğiyle çalışacağını belirtir.
        // Tam adres GET /api/documents olur.
        [HttpGet]

        // Kullanıcının kendi belgelerini PostgreSQL’den getirir.
        public async Task<IActionResult> GetDocuments()
        {
            // JWT token içerisinden kullanıcı ID bilgisini alır.
            var userIdClaim = User
                .FindFirst(ClaimTypes.NameIdentifier)?
                .Value;


            // Token içinde kullanıcı ID’si bulunmuyorsa oturum geçersizdir.
            if (string.IsNullOrEmpty(userIdClaim))
            {
                // 401 Unauthorized cevabı döndürür.
                return Unauthorized();
            }


            // Token içindeki kullanıcı ID’sini int türüne çevirmeyi dener.
            if (!int.TryParse(userIdClaim, out var userId))
            {
                // ID sayıya çevrilemiyorsa geçersiz oturum mesajı döndürür.
                return Unauthorized(new
                {
                    Message = "Kullanıcı oturumu geçersiz."
                });
            }


            // Documents tablosunda sorgu oluşturmaya başlar.
            var documents = await _context.Documents

                // Yalnızca giriş yapan kullanıcıya ait belgeleri seçer.
                .Where(d => d.UserId == userId)

                // Belgeleri yüklenme tarihine göre yeniden eskiye doğru sıralar.
                .OrderByDescending(d => d.UploadDate)

                // Document nesnelerini frontend’e uygun DocumentDto nesnelerine dönüştürür.
                .Select(d => new DocumentDto
                {
                    // Belgenin ID bilgisini aktarır.
                    Id = d.Id,

                    // Belgenin başlığını aktarır.
                    Title = d.Title,

                    // Belgenin dosya adını aktarır.
                    FileName = d.FileName,

                    // Belgenin dosya türünü aktarır.
                    FileType = d.FileType,

                    // Belgenin dosya boyutunu aktarır.
                    FileSize = d.FileSize,

                    // Belgenin yüklenme tarihini aktarır.
                    UploadDate = d.UploadDate,

                    // Belgenin indeksleme durumunu aktarır.
                    IndexingStatus = d.IndexingStatus
                })

                // Hazırlanan sorguyu çalıştırır ve sonuçları listeye dönüştürür.
                .ToListAsync();


            // Kullanıcının belgelerini frontend’e 200 OK ile gönderir.
            return Ok(documents);
        }


        /// <summary>
        /// Belgeyi veritabanından ve sunucudaki fiziksel klasörden siler.
        /// </summary>

        // URL içinde silinecek belgenin ID’sini alır.
        // Örneğin DELETE /api/documents/5 isteğinde id değeri 5 olur.
        [HttpDelete("{id}")]

        // Belge ID’sini parametre olarak alıp silme işlemini gerçekleştirir.
        public async Task<IActionResult> DeleteDocument(int id)
        {
            // JWT token içerisinden kullanıcı ID bilgisini alır.
            var userIdClaim = User
                .FindFirst(ClaimTypes.NameIdentifier)?
                .Value;


            // Token içinde kullanıcı ID’si yoksa işlemi durdurur.
            if (string.IsNullOrEmpty(userIdClaim))
            {
                return Unauthorized();
            }


            // Kullanıcı ID’sini int türüne çevirmeyi dener.
            if (!int.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new
                {
                    Message = "Kullanıcı oturumu geçersiz."
                });
            }


            // Documents tablosunda hem belge ID’si hem kullanıcı ID’si eşleşen kaydı arar.
            // Kullanıcı böylece başka bir kullanıcıya ait belgeyi silemez.
            var document = await _context.Documents
                .FirstOrDefaultAsync(
                    d => d.Id == id &&
                         d.UserId == userId);


            // Belge bulunamadıysa veya başka kullanıcıya aitse document null olur.
            if (document == null)
            {
                // Frontend’e 404 Not Found cevabı gönderir.
                return NotFound(new
                {
                    Message =
                        "Belge bulunamadı veya bu belge üzerinde işlem yapma yetkiniz yok."
                });
            }


            // Önce belgenin Qdrant içindeki vektörlerini silmeyi dener.
            try
            {
                // Belge ID’sine ait bütün chunk vektörlerini Qdrant’tan siler.
                await _qdrantService.DeleteDocumentChunksAsync(
                    document.Id);
            }
            // Yalnızca bağlantı veya zaman aşımı hatalarında bu blok çalışır.
            catch (Exception exception) when (
                exception is HttpRequestException or
                TaskCanceledException)
            {
                // Belge daha önce Qdrant’a başarılı şekilde kaydedilmişse çalışır.
                if (document.IndexingStatus == "Ready")
                {
                    // Qdrant temizliği başarısız olduğu için hatayı loglara kaydeder.
                    _logger.LogError(
                        exception,
                        "Belge {DocumentId} için Qdrant temizliği başarısız oldu.",
                        document.Id);

                    // Qdrant’a ulaşılamadığı için belgeyi yerel kayıtlardan da silmez.
                    // 503 Service Unavailable cevabı döndürür.
                    return StatusCode(
                        StatusCodes.Status503ServiceUnavailable,
                        new
                        {
                            Message =
                                "Belge şu anda silinemiyor. Qdrant servisine ulaşılamadı."
                        });
                }


                // Belge Qdrant’a hiç kaydedilmemişse yalnızca uyarı kaydı oluşturur.
                _logger.LogWarning(
                    exception,
                    "İndekslenmemiş belge {DocumentId}, Qdrant kapalı olmasına rağmen yerel kayıtlardan siliniyor.",
                    document.Id);
            }


            // PDF dosyasının sunucuda bulunup bulunmadığını kontrol eder.
            if (System.IO.File.Exists(document.FilePath))
            {
                // Fiziksel PDF dosyasını Uploads klasöründen siler.
                System.IO.File.Delete(document.FilePath);
            }


            // Belge kaydını PostgreSQL’den silinecekler listesine ekler.
            // İlişkili chunk’lar cascade ayarıyla beraber silinebilir.
            _context.Documents.Remove(document);

            // Silme işlemini PostgreSQL’e kaydeder.
            await _context.SaveChangesAsync();


            // Başarılı silme sonucunu frontend’e gönderir.
            return Ok(new
            {
                Message = "Belge başarıyla silindi."
            });
        }


        /// <summary>
        /// Daha önce indekslenemeyen bir belgenin mevcut parçalarını
        /// yeniden Qdrant'a kaydeder.
        /// </summary>

        // Belge ID’sini URL içinden alır.
        // Örneğin POST /api/documents/5/reindex isteğinde id değeri 5 olur.
        [HttpPost("{id}/reindex")]

        // Belgenin embedding ve Qdrant kayıt işlemini yeniden gerçekleştirir.
        public async Task<IActionResult> ReindexDocument(int id)
        {
            // JWT token içerisinden kullanıcı ID bilgisini alır.
            var userIdClaim = User
                .FindFirst(ClaimTypes.NameIdentifier)?
                .Value;


            // Kullanıcı ID’sini sayıya çevirmeyi dener.
            if (!int.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new
                {
                    Message = "Kullanıcı oturumu geçersiz."
                });
            }


            // Veritabanında belirtilen belgeyi aramaya başlar.
            var document = await _context.Documents

                // Belgeyle ilişkili chunk kayıtlarını da sorguya dahil eder.
                .Include(d => d.Chunks)

                // Hem belge ID’sinin hem kullanıcı ID’sinin eşleşmesini ister.
                .FirstOrDefaultAsync(
                    d => d.Id == id &&
                         d.UserId == userId);


            // Belge bulunamadıysa veya kullanıcıya ait değilse bu blok çalışır.
            if (document == null)
            {
                return NotFound(new
                {
                    Message =
                        "Belge bulunamadı veya bu belge üzerinde işlem yapma yetkiniz yok."
                });
            }


            // Belgenin chunk’larını sıralamaya başlar.
            var chunks = document.Chunks

                // Chunk’ları belge içindeki sırasına göre sıralar.
                .OrderBy(chunk => chunk.ChunkIndex)

                // Sıralanan kayıtları listeye dönüştürür.
                .ToList();


            // Belgeye ait hiç chunk yoksa yeniden indeksleme yapılamaz.
            if (chunks.Count == 0)
            {
                // Belgenin işlenecek içeriği olmadığını belirtir.
                document.IndexingStatus = "NoContent";

                // Durumun nedenini hata alanına kaydeder.
                document.IndexingError =
                    "Belgeden işlenecek metin çıkarılamadı.";

                // Yeni durumu PostgreSQL’e kaydeder.
                await _context.SaveChangesAsync();


                // Frontend’e 400 Bad Request cevabı döndürür.
                return BadRequest(new
                {
                    Message = document.IndexingError
                });
            }


            // Yeniden indeksleme başladığı için durumu Pending yapar.
            document.IndexingStatus = "Pending";

            // Önceki indeksleme hata mesajını temizler.
            document.IndexingError = null;

            // Yeni durumu PostgreSQL’e kaydeder.
            await _context.SaveChangesAsync();


            // Ollama ve Qdrant işlemlerini güvenli şekilde çalıştırır.
            try
            {
                // Daha önce yarım kalmış Qdrant kayıtlarını siler.
                // Böylece aynı chunk’ın iki kez kaydedilmesi önlenir.
                await _qdrantService.DeleteDocumentChunksAsync(
                    document.Id);


                // Mevcut chunk’lar için yeniden embedding oluşturur.
                var embeddings =
                    await GenerateEmbeddingsInBatchesAsync(chunks);


                // Chunk ve embedding değerlerini yeniden Qdrant’a kaydeder.
                await _qdrantService.SaveChunksAsync(
                    chunks,
                    embeddings);


                // İşlem başarılı olduğu için belgeyi hazır olarak işaretler.
                document.IndexingStatus = "Ready";

                // Hata alanını temiz bırakır.
                document.IndexingError = null;

                // Başarılı indeksleme durumunu PostgreSQL’e kaydeder.
                await _context.SaveChangesAsync();


                // Document nesnesini DTO’ya dönüştürüp frontend’e gönderir.
                return Ok(ToDocumentDto(document));
            }
            // Ollama veya Qdrant işlemlerinde herhangi bir hata olursa çalışır.
            catch (Exception exception)
            {
                // Hatanın ayrıntılarını loglara kaydeder.
                _logger.LogError(
                    exception,
                    "Belge {DocumentId} yeniden indekslenemedi.",
                    document.Id);


                // Belgenin indeksleme durumunu başarısız olarak değiştirir.
                document.IndexingStatus = "Failed";

                // Hata mesajını en fazla 1000 karakter olacak şekilde kaydeder.
                document.IndexingError =
                    LimitIndexingError(exception);

                // Başarısızlık durumunu PostgreSQL’e kaydeder.
                await _context.SaveChangesAsync();


                // Servislere ulaşılamadığını belirten 503 cevabı döndürür.
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new
                    {
                        Message =
                            "Belge indekslenemedi. Ollama ve Qdrant servislerini kontrol edin."
                    });
            }
        }


        // Chunk’ların embedding değerlerini gruplar hâlinde oluşturur.
        // private olduğu için yalnızca DocumentsController içinde kullanılabilir.
        private async Task<List<float[]>>
            GenerateEmbeddingsInBatchesAsync(List<Chunk> chunks)
        {
            // Oluşturulan embedding değerlerinin tutulacağı listeyi hazırlar.
            // Listenin başlangıç kapasitesini chunk sayısına göre ayarlar.
            var embeddings =
                new List<float[]>(chunks.Count);


            // Chunk listesini EmbeddingBatchSize kadar gruplara ayırır.
            // EmbeddingBatchSize 4 olduğu için her turda en fazla dört chunk işlenir.
            foreach (var batch in chunks.Chunk(EmbeddingBatchSize))
            {
                // Gruptaki chunk’ların embedding’lerini aynı anda oluşturur.
                // Task.WhenAll tüm işlemlerin tamamlanmasını bekler.
                var batchEmbeddings = await Task.WhenAll(
                    batch.Select(
                        chunk =>
                            _ollamaService.GetEmbeddingAsync(
                                chunk.Content)));


                // Oluşturulan embedding grubunu ana listeye ekler.
                embeddings.AddRange(batchEmbeddings);
            }


            // Bütün chunk’ların embedding listesini çağıran metoda döndürür.
            return embeddings;
        }


        // Document nesnesini frontend’e gönderilecek DocumentDto nesnesine dönüştürür.
        // static olduğu için sınıfın servis alanlarına ihtiyaç duymaz.
        private static DocumentDto ToDocumentDto(Document document)
        {
            // Yeni bir DocumentDto oluşturup gerekli belge bilgilerini aktarır.
            return new DocumentDto
            {
                // Belgenin ID bilgisini aktarır.
                Id = document.Id,

                // Belgenin başlığını aktarır.
                Title = document.Title,

                // Belgenin dosya adını aktarır.
                FileName = document.FileName,

                // Belgenin dosya türünü aktarır.
                FileType = document.FileType,

                // Belgenin dosya boyutunu aktarır.
                FileSize = document.FileSize,

                // Belgenin yüklenme tarihini aktarır.
                UploadDate = document.UploadDate,

                // Belgenin indeksleme durumunu aktarır.
                IndexingStatus = document.IndexingStatus
            };
        }


        // İndeksleme sırasında oluşan hata mesajını güvenli uzunluğa indirir.
        private static string LimitIndexingError(Exception exception)
        {
            // Hatanın en temel sebebine ait mesajı alır.
            var message =
                exception.GetBaseException().Message;

            // Mesaj 1000 karakterden uzunsa ilk 1000 karakteri döndürür.
            // Daha kısaysa mesajın tamamını döndürür.
            return message[
                ..Math.Min(1000, message.Length)];
        }


        // Yüklenen dosyanın başlangıç byte’larını kontrol eder.
        // Gerçek bir PDF dosyasının "%PDF-" imzasıyla başlaması beklenir.
        private static async Task<bool>
            HasPdfSignatureAsync(IFormFile file)
        {
            // PDF imzasını okumak için 5 byte uzunluğunda bir dizi oluşturur.
            var signature = new byte[5];


            // Yüklenen dosyayı okuma amacıyla açar.
            // await using işlem bitince dosya akışını otomatik kapatır.
            await using var stream =
                file.OpenReadStream();


            // Dosyanın ilk 5 byte’ını signature dizisine okumayı dener.
            var bytesRead = await stream.ReadAtLeastAsync(
                signature,
                signature.Length,
                throwOnEndOfStream: false);


            // Tam olarak 5 byte okunup okunmadığını kontrol eder.
            // Sonra bu byte’ların "%PDF-" değerine eşit olup olmadığını karşılaştırır.
            return bytesRead == signature.Length

                // 0x25, yüzde işaretinin byte değeridir.
                && signature[0] == 0x25

                // 0x50, büyük P harfinin byte değeridir.
                && signature[1] == 0x50

                // 0x44, büyük D harfinin byte değeridir.
                && signature[2] == 0x44

                // 0x46, büyük F harfinin byte değeridir.
                && signature[3] == 0x46

                // 0x2D, tire işaretinin byte değeridir.
                && signature[4] == 0x2D;
        }
    }
}




/*

Bu dosyanın genel akışı:

PDF yükleme
→ Dosya kontrolleri
→ Kullanıcı ID’sini JWT’den alma
→ PDF’yi Uploads klasörüne kaydetme
→ Belgeyi PostgreSQL’e kaydetme
→ PDF metnini chunk’lara ayırma
→ Chunk’ları PostgreSQL’e kaydetme
→ Ollama ile embedding oluşturma
→ Embedding’leri Qdrant’a kaydetme
→ Belgeyi Ready olarak işaretleme

*/