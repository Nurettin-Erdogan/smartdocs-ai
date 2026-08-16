using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.DTOs;
using SmartDocsAI.API.Helpers;
using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Models;

namespace SmartDocsAI.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("AuthPolicy")]
public sealed class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITokenService _tokenService;

    public AuthController(AppDbContext context, ITokenService tokenService)
    {
        _context = context;
        _tokenService = tokenService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        RegisterDto registerDto,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = registerDto.Email.Trim().ToLowerInvariant();
        var normalizedFullName = registerDto.FullName.Trim();

        if (string.IsNullOrWhiteSpace(normalizedFullName))
        {
            return BadRequest(new { Message = "Ad Soyad alanı boş bırakılamaz." });
        }

        if (await _context.Users.AnyAsync(
                user => user.Email == normalizedEmail,
                cancellationToken))
        {
            return Conflict(new { Message = "Bu e-posta adresi zaten kullanımda." });
        }

        var user = new User
        {
            FullName = normalizedFullName,
            Email = normalizedEmail,
            PasswordHash = PasswordHasher.HashPassword(registerDto.Password),
            RoleId = 2,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            return Conflict(new { Message = "Bu e-posta adresi zaten kullanımda." });
        }

        user.Role = await _context.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(role => role.Id == user.RoleId, cancellationToken);

        return Ok(CreateAuthResponse(user));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginDto loginDto,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = loginDto.Email.Trim().ToLowerInvariant();
        var user = await _context.Users
            .AsNoTracking()
            .Include(item => item.Role)
            .FirstOrDefaultAsync(
                item => item.Email == normalizedEmail,
                cancellationToken);

        if (user is null ||
            !PasswordHasher.VerifyPassword(loginDto.Password, user.PasswordHash))
        {
            return Unauthorized(new { Message = "Geçersiz e-posta veya şifre." });
        }

        return Ok(CreateAuthResponse(user));
    }

    private object CreateAuthResponse(User user) => new
    {
        user.Id,
        user.FullName,
        user.Email,
        Role = user.Role?.Name,
        Token = _tokenService.CreateToken(user)
    };
}
