using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.DTOs;
using SmartDocsAI.API.Helpers;
using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Models;

namespace SmartDocsAI.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ITokenService _tokenService;

        // Bağımlılık Enjeksiyonu (Dependency Injection - DI) kullanarak DbContext ve TokenService alıyoruz.
        public AuthController(AppDbContext context, ITokenService tokenService)
        {
            _context = context;
            _tokenService = tokenService;
        }

        /// <summary>
        /// Yeni kullanıcı kaydı oluşturur.
        /// </summary>
        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto registerDto)
        {
            // E-posta adresinin sistemde eşsiz olup olmadığını kontrol ediyoruz.
            if (await _context.Users.AnyAsync(u => u.Email == registerDto.Email.ToLower()))
            {
                return BadRequest(new { Message = "Bu e-posta adresi zaten kullanımda." });
            }

            // Yeni kullanıcı modelini dolduruyoruz (Şifreyi hashleyerek!)
            var user = new User
            {
                FullName = registerDto.FullName,
                Email = registerDto.Email.ToLower(),
                PasswordHash = PasswordHasher.HashPassword(registerDto.Password),
                RoleId = 2, // Varsayılan Rol: Personel (Seed verilerimizde ID 2)
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // İlişkili Rol adını yükleyip JWT oluşturuyoruz.
            user.Role = await _context.Roles.FindAsync(user.RoleId);
            var token = _tokenService.CreateToken(user);

            return Ok(new
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role?.Name,
                Token = token
            });
        }

        /// <summary>
        /// Kullanıcı girişi yapar ve JWT Token döner.
        /// </summary>
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto loginDto)
        {
            // Kullanıcıyı veritabanında e-postasıyla arıyoruz ve Rol ilişkisini de dahil ediyoruz (Include).
            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Email == loginDto.Email.ToLower());

            // Kullanıcı bulunamadıysa yetkisiz hatası dönüyoruz.
            if (user == null)
            {
                return Unauthorized(new { Message = "Geçersiz e-posta veya şifre." });
            }

            // Şifreyi doğruluyoruz.
            if (!PasswordHasher.VerifyPassword(loginDto.Password, user.PasswordHash))
            {
                return Unauthorized(new { Message = "Geçersiz e-posta veya şifre." });
            }

            // Doğrulama başarılıysa Token oluşturup kullanıcı bilgilerini dönüyoruz.
            var token = _tokenService.CreateToken(user);

            return Ok(new
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role?.Name,
                Token = token
            });
        }
    }
}