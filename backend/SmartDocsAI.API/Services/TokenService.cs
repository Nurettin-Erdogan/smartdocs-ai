using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Models;

namespace SmartDocsAI.API.Services;

public sealed class TokenService : ITokenService
{
    private readonly IConfiguration _configuration;
    private readonly SymmetricSecurityKey _key;
    private readonly int _lifetimeMinutes;

    public TokenService(IConfiguration configuration)
    {
        _configuration = configuration;
        var tokenKey = configuration["JwtSettings:TokenKey"]
            ?? throw new InvalidOperationException("JwtSettings:TokenKey is missing.");

        if (Encoding.UTF8.GetByteCount(tokenKey) < 64)
        {
            throw new InvalidOperationException(
                "JwtSettings:TokenKey must be at least 64 bytes for HMAC-SHA512.");
        }

        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenKey));
        _lifetimeMinutes = Math.Clamp(
            configuration.GetValue<int?>("JwtSettings:LifetimeMinutes") ?? 480,
            15,
            10_080);
    }

    public string CreateToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.NameId, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.Name, user.FullName)
        };

        if (user.Role is not null)
        {
            claims.Add(new Claim(ClaimTypes.Role, user.Role.Name));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(_lifetimeMinutes),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha512Signature),
            Issuer = _configuration["JwtSettings:Issuer"],
            Audience = _configuration["JwtSettings:Audience"]
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
