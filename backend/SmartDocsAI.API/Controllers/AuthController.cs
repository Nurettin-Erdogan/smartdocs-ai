// ASP.NET Core Web API araçlarını kullanmamızı sağlar.
// ControllerBase, Route, HttpPost, Ok ve Unauthorized gibi yapılar buradan gelir.
using Microsoft.AspNetCore.Mvc;

// Kayıt ve giriş işlemlerine istek sınırı koymamızı sağlar.
// Böylece kısa sürede çok fazla giriş denemesi yapılması engellenebilir.
using Microsoft.AspNetCore.RateLimiting;

// Entity Framework Core ile veritabanı sorguları yapmamızı sağlar.
// AnyAsync, Include ve FirstOrDefaultAsync gibi metotlar buradan gelir.
using Microsoft.EntityFrameworkCore;

// Projemizdeki AppDbContext sınıfını kullanmamızı sağlar.
// AppDbContext, backend ile PostgreSQL arasındaki bağlantıyı yönetir.
using SmartDocsAI.API.Data;

// Frontend’den gelen kayıt ve giriş verilerini taşıyan DTO sınıflarını kullanmamızı sağlar.
// Bu controller içinde RegisterDto ve LoginDto kullanılacaktır.
using SmartDocsAI.API.DTOs;

// Parola işlemlerinde kullandığımız yardımcı sınıfları kullanmamızı sağlar.
// PasswordHasher sınıfı parolayı hashlemek ve kontrol etmek için kullanılır.
using SmartDocsAI.API.Helpers;

// Projedeki interface sınıflarını kullanmamızı sağlar.
// Burada JWT üreten ITokenService interface’i kullanılacaktır.
using SmartDocsAI.API.Interfaces;

// User ve Role gibi veritabanı modellerini kullanmamızı sağlar.
// Bu controller içinde yeni bir User nesnesi oluşturacağız.
using SmartDocsAI.API.Models;


// Bu dosyadaki AuthController sınıfının Controllers grubuna ait olduğunu belirtir.
// Namespace, proje içindeki sınıfların düzenli biçimde gruplanmasını sağlar.
namespace SmartDocsAI.API.Controllers
{
    // Bu sınıfın bir Web API controller’ı olduğunu ASP.NET Core’a bildirir.
    // Gelen JSON verilerinin DTO’lara dönüştürülmesi gibi bazı işlemleri otomatikleştirir.
    [ApiController]

    // Bu controller’ın temel API adresini belirler.
    // AuthController isminden dolayı bu adres "/api/auth" olur.
    [Route("api/[controller]")]

    // Bu controller içindeki endpointlere AuthPolicy isimli istek sınırını uygular.
    // AuthPolicy’nin ayrıntıları Program.cs dosyasında tanımlanmıştır.
    [EnableRateLimiting("AuthPolicy")]

    // Kullanıcı kayıt ve giriş işlemlerini yöneten controller sınıfıdır.
    // ControllerBase’den kalıtım aldığı için Ok(), BadRequest() ve Unauthorized() kullanabilir.
    public class AuthController : ControllerBase
    {
        // PostgreSQL veritabanıyla iletişim kurmak için kullanılacak nesnedir.
        // Kullanıcı arama, kullanıcı ekleme ve rol bulma işlemleri bununla yapılır.
        private readonly AppDbContext _context;

        // Kullanıcı için JWT token oluşturacak servis nesnesidir.
        // Token sayesinde sonraki isteklerin hangi kullanıcıya ait olduğu anlaşılır.
        private readonly ITokenService _tokenService;


        // AuthController oluşturulurken ihtiyaç duyduğu servisleri dışarıdan alır.
        // Bu yönteme Dependency Injection, yani bağımlılık enjeksiyonu denir.
        public AuthController(
            // PostgreSQL veritabanı ve tablolarıyla çalışmamızı sağlayacak nesnedir.
            AppDbContext context,

            // Kullanıcı için JWT token oluşturacak servis nesnesidir.
            ITokenService tokenService)
        {
            // Dışarıdan gelen context nesnesini sınıfın _context alanına kaydeder.
            // Böylece aşağıdaki kayıt ve giriş metotlarında veritabanını kullanabiliriz.
            _context = context;

            // Dışarıdan gelen tokenService nesnesini _tokenService alanına kaydeder.
            // Böylece kullanıcı giriş yaptığında JWT token oluşturabiliriz.
            _tokenService = tokenService;
        }


        /// <summary>
        /// Sisteme yeni bir kullanıcı kaydeder.
        /// Kayıt başarılı olursa kullanıcı bilgileriyle birlikte JWT token döndürür.
        /// </summary>

        // Bu metodun bir HTTP POST endpointi olduğunu belirtir.
        // Tam endpoint adresi "POST /api/auth/register" olur.
        [HttpPost("register")]

        // RegisterDto ile frontend’den ad, e-posta ve parola bilgilerini alır.
        // async olduğu için veritabanı işlemlerini sistemi bekletmeden çalıştırabilir.
        public async Task<IActionResult> Register(RegisterDto registerDto)
        {
            // Frontend’den gelen e-postanın başındaki ve sonundaki boşlukları siler.
            // Ayrıca e-postayı küçük harfe çevirerek standart bir biçime getirir.
            var normalizedEmail = registerDto.Email
                .Trim()
                .ToLowerInvariant();

            // Frontend’den gelen ad ve soyadın başındaki ve sonundaki boşlukları siler.
            // Örneğin "  Nurettin Erdoğan  " değeri "Nurettin Erdoğan" olur.
            var normalizedFullName = registerDto.FullName.Trim();


            // Users tablosunda aynı e-posta adresine sahip bir kullanıcı var mı kontrol eder.
            // u, veritabanındaki her bir kullanıcıyı geçici olarak temsil eder.
            if (await _context.Users.AnyAsync(
                    u => u.Email == normalizedEmail))
            {
                // Aynı e-posta daha önce kullanılmışsa yeni kullanıcı oluşturulmaz.
                // Frontend’e 400 Bad Request kodu ve açıklama mesajı gönderilir.
                return BadRequest(new
                {
                    // Frontend’in kullanıcıya gösterebileceği hata mesajıdır.
                    Message = "Bu e-posta adresi zaten kullanımda."
                });
            }


            // Veritabanına kaydedilecek yeni bir User nesnesi oluşturur.
            // Süslü parantezlerin içinde kullanıcının alanları doldurulur.
            var user = new User
            {
                // Kullanıcının temizlenmiş ad ve soyadını User nesnesine aktarır.
                FullName = normalizedFullName,

                // Kullanıcının standart hâle getirilmiş e-posta adresini aktarır.
                Email = normalizedEmail,

                // Kullanıcının parolasını doğrudan veritabanına kaydetmez.
                // PasswordHasher ile hashleyerek güvenli bir değere dönüştürür.
                PasswordHash = PasswordHasher.HashPassword(
                    registerDto.Password),

                // Yeni kayıt olan kullanıcıya varsayılan olarak 2 numaralı rolü verir.
                // Seed verilerinde 2 numaralı rol normal kullanıcı veya personel rolüdür.
                RoleId = 2,

                // Kullanıcının kayıt olduğu tarihi UTC saatine göre kaydeder.
                // UTC kullanılması farklı ülkelerin saatlerinden kaynaklanan sorunları azaltır.
                CreatedAt = DateTime.UtcNow
            };


            // Hazırlanan user nesnesini Users tablosuna eklenecekler listesine koyar.
            // Bu satır henüz veritabanına kesin olarak kayıt yapmaz.
            _context.Users.Add(user);

            // Bekleyen değişiklikleri PostgreSQL veritabanına kaydeder.
            // Bu işlemden sonra veritabanı kullanıcıya otomatik bir Id verir.
            await _context.SaveChangesAsync();


            // Kullanıcının RoleId değerine karşılık gelen rolü Roles tablosunda bulur.
            // Bulunan Role nesnesi user nesnesinin Role alanına yerleştirilir.
            user.Role = await _context.Roles.FindAsync(user.RoleId);

            // Yeni oluşturulan kullanıcı bilgileriyle bir JWT token üretir.
            // Token içinde kullanıcının kimliğini belirlemeye yarayan bilgiler bulunur.
            var token = _tokenService.CreateToken(user);


            // Kayıt başarılı olduğu için frontend’e 200 OK cevabı döndürür.
            // Cevabın içinde kullanıcı bilgileri ve JWT token bulunur.
            return Ok(new
            {
                // Veritabanının yeni kullanıcıya verdiği benzersiz kimlik numarasıdır.
                Id = user.Id,

                // Kullanıcının ad ve soyadını frontend’e gönderir.
                FullName = user.FullName,

                // Kullanıcının e-posta adresini frontend’e gönderir.
                Email = user.Email,

                // Kullanıcının rol adını frontend’e gönderir.
                // Role bulunamazsa soru işareti sayesinde hata oluşmadan null döner.
                Role = user.Role?.Name,

                // Oluşturulan JWT tokenı frontend’e gönderir.
                // Frontend sonraki yetki gerektiren işlemlerde bu tokenı kullanır.
                Token = token
            });
        }


        /// <summary>
        /// Sistemde kayıtlı bir kullanıcının giriş işlemini yapar.
        /// E-posta ve parola doğruysa kullanıcı bilgileriyle JWT token döndürür.
        /// </summary>

        // Bu metodun bir HTTP POST endpointi olduğunu belirtir.
        // Tam endpoint adresi "POST /api/auth/login" olur.
        [HttpPost("login")]

        // LoginDto üzerinden frontend’den e-posta ve parola bilgilerini alır.
        // İşlem sonunda bir HTTP cevabı döndürür.
        public async Task<IActionResult> Login(LoginDto loginDto)
        {
            // Frontend’den gelen e-postanın başındaki ve sonundaki boşlukları siler.
            // E-postayı küçük harfe çevirerek veritabanındaki biçimle aynı hâle getirir.
            var normalizedEmail = loginDto.Email
                .Trim()
                .ToLowerInvariant();


            // Users tablosunda verilen e-posta adresine sahip kullanıcıyı aramaya başlar.
            var user = await _context.Users

                // Kullanıcı bulunurken kullanıcının ilişkili Role bilgisini de sorguya ekler.
                // Böylece daha sonra rol bilgisi için tekrar sorgu yapılmasına gerek kalmaz.
                .Include(u => u.Role)

                // E-postası normalizedEmail değerine eşit olan ilk kullanıcıyı getirir.
                // Kullanıcı bulunamazsa user değişkeninin değeri null olur.
                .FirstOrDefaultAsync(
                    u => u.Email == normalizedEmail);


            // Veritabanında bu e-posta adresine sahip kullanıcı bulunamadıysa çalışır.
            // user == null, kullanıcı nesnesinin oluşmadığı anlamına gelir.
            if (user == null)
            {
                // Kullanıcı bulunamadığında 401 Unauthorized cevabı döndürür.
                // Güvenlik için e-postanın mı parolanın mı yanlış olduğu ayrı ayrı söylenmez.
                return Unauthorized(new
                {
                    // Frontend’in kullanıcıya göstereceği genel hata mesajıdır.
                    Message = "Geçersiz e-posta veya şifre."
                });
            }


            // Kullanıcının yazdığı parola ile veritabanındaki parola hashini karşılaştırır.
            // VerifyPassword eşleşme varsa true, eşleşme yoksa false döndürür.
            if (!PasswordHasher.VerifyPassword(
                    loginDto.Password,
                    user.PasswordHash))
            {
                // Baştaki ünlem işareti sonucu tersine çevirir.
                // Parola doğrulanmadıysa bu bloğun içine girilir.

                // Yanlış parola durumunda 401 Unauthorized cevabı döndürülür.
                return Unauthorized(new
                {
                    // E-posta bulunamadığında verilen mesajla aynı mesaj kullanılır.
                    // Böylece kötü niyetli kişiler hangi e-postaların kayıtlı olduğunu anlayamaz.
                    Message = "Geçersiz e-posta veya şifre."
                });
            }


            // Kullanıcı bulunduğu ve parola doğru olduğu için JWT token oluşturur.
            // Bu token sonraki yetki gerektiren API isteklerinde kullanılacaktır.
            var token = _tokenService.CreateToken(user);


            // Giriş başarılı olduğu için frontend’e 200 OK cevabı gönderir.
            // Cevapta kullanıcı bilgileri ve oluşturulan JWT token bulunur.
            return Ok(new
            {
                // Giriş yapan kullanıcının veritabanındaki benzersiz kimlik numarasıdır.
                Id = user.Id,

                // Giriş yapan kullanıcının ad ve soyadını frontend’e gönderir.
                FullName = user.FullName,

                // Giriş yapan kullanıcının e-posta adresini frontend’e gönderir.
                Email = user.Email,

                // Kullanıcının rol adını frontend’e gönderir.
                // Rol bilgisi yoksa hata vermeden null değeri döndürülebilir.
                Role = user.Role?.Name,

                // Kullanıcı için oluşturulan JWT tokenı frontend’e gönderir.
                Token = token
            });
        }
    }
}

/*
    Kodun tamamının yaptığı iş

    Kayıt olurken:

    Frontend ad, e-posta ve parola gönderir
    → E-posta düzenlenir
    → Aynı e-posta daha önce kullanılmış mı kontrol edilir
    → Parola hashlenir
    → Kullanıcı PostgreSQL’e kaydedilir
    → JWT token oluşturulur
    → Kullanıcı bilgileri frontend’e döndürülür

    Giriş yaparken:

    Frontend e-posta ve parola gönderir
    → Kullanıcı PostgreSQL’de aranır
    → Parola doğrulanır
    → JWT token oluşturulur
    → Kullanıcı bilgileri frontend’e döndürülür             */