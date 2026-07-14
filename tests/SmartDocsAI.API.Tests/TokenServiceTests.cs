using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using SmartDocsAI.API.Models;
using SmartDocsAI.API.Services;

namespace SmartDocsAI.API.Tests;

public sealed class TokenServiceTests
{
    [Fact]
    public void CreateToken_WritesIdentityRoleAndConfiguredLifetime()
    {
        var service = CreateService(lifetimeMinutes: 60);
        var user = new User
        {
            Id = 42,
            FullName = "Test Kullanıcısı",
            Email = "test@example.com",
            Role = new Role { Id = 2, Name = "Personel" }
        };

        var token = new JwtSecurityTokenHandler().ReadJwtToken(service.CreateToken(user));

        Assert.Equal("42", token.Claims.Single(claim => claim.Type == "nameid").Value);
        Assert.Equal("test@example.com", token.Claims.Single(claim => claim.Type == "email").Value);
        Assert.Equal("Personel", token.Claims.Single(claim => claim.Type == "role").Value);
        Assert.InRange(token.ValidTo - token.ValidFrom, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(61));
    }

    [Fact]
    public void Constructor_RejectsShortSigningKey()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:TokenKey"] = "too-short",
                ["JwtSettings:Issuer"] = "SmartDocsAI",
                ["JwtSettings:Audience"] = "SmartDocsAIUsers"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => new TokenService(configuration));
    }

    private static TokenService CreateService(int lifetimeMinutes)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:TokenKey"] = new string('k', 64),
                ["JwtSettings:Issuer"] = "SmartDocsAI",
                ["JwtSettings:Audience"] = "SmartDocsAIUsers",
                ["JwtSettings:LifetimeMinutes"] = lifetimeMinutes.ToString()
            })
            .Build();

        return new TokenService(configuration);
    }
}
