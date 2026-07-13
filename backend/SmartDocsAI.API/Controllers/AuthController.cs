using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.DTOs;
using SmartDocsAI.API.Helpers;
using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Models;

namespace SmartDocsAI.API.Controllers
{
    // Kullanıcı kayıt ve giriş işlemlerini yönetir.
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("AuthPolicy")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ITokenService _tokenService;

        // Gerekli servisler Dependency Injection ile alınır.
        public AuthController(AppDbContext context, ITokenService tokenService)
        {
            _context = context;
            _tokenService = tokenService;
        }

        /// <summary>
        /// Yeni kullanıcı kaydı oluşturur ve JWT token döndürür.
        /// </summary>
        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto registerDto)
        {
            // E-posta ve ad bilgilerini standart hâle getirir.
            var normalizedEmail = registerDto.Email.Trim().ToLowerInvariant();
            var normalizedFullName = registerDto.FullName.Trim();

            // Aynı e-posta adresiyle ikinci kez kayıt yapılmasını engeller.
            if (await _context.Users.AnyAsync(u => u.Email == normalizedEmail))
            {
                return BadRequest(new { Message = "Bu e-posta adresi zaten kullanımda." });
            }

            // Parolayı hashleyerek yeni kullanıcıyı hazırlar.
            var user = new User
            {
                FullName = normalizedFullName,
                Email = normalizedEmail,
                PasswordHash = PasswordHasher.HashPassword(registerDto.Password),
                RoleId = 2,
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Rol bilgisini yükler ve kullanıcı için JWT oluşturur.
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
        /// Kullanıcı bilgilerini doğrular ve JWT token döndürür.
        /// </summary>
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto loginDto)
        {
            var normalizedEmail = loginDto.Email.Trim().ToLowerInvariant();

            // Kullanıcıyı e-posta adresiyle ve rol bilgisiyle birlikte bulur.
            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Email == normalizedEmail);

            if (user == null)
            {
                return Unauthorized(new { Message = "Geçersiz e-posta veya şifre." });
            }

            // Girilen parola ile kayıtlı parola hashini karşılaştırır.
            if (!PasswordHasher.VerifyPassword(loginDto.Password, user.PasswordHash))
            {
                return Unauthorized(new { Message = "Geçersiz e-posta veya şifre." });
            }

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
