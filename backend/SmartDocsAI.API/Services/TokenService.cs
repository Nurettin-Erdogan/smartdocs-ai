using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Models;

namespace SmartDocsAI.API.Services
{
    public class TokenService : ITokenService
    {
        private readonly IConfiguration _config;
        private readonly SymmetricSecurityKey _key;

        public TokenService(IConfiguration config)
        {
            _config = config;

            // appsettings.json dosyasından TokenKey (şifreleme anahtarını) alıyoruz.
            var tokenKey = _config["JwtSettings:TokenKey"]
                ?? throw new ArgumentNullException("JwtSettings:TokenKey appsettings.json'da tanımlanmamış.");

            // Anahtarı byte dizisine çevirip şifreleme sınıfına veriyoruz.
            _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenKey));
        }

        public string CreateToken(User user)
        {
            // Token içerisine gömeceğimiz kullanıcı bilgileri (Claims)
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.NameId, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.Name, user.FullName)
            };

            // Eğer kullanıcının rolü yüklenmişse rol bilgisini de Token'a ekliyoruz.
            if (user.Role != null)
            {
                claims.Add(new Claim(ClaimTypes.Role, user.Role.Name));
            }

            // Token'ı imzalamak için HmacSha512 algoritmasını kullanıyoruz.
            var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha512Signature);

            // Token'ın detaylarını yapılandırıyoruz.
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddDays(7), // Token 7 gün geçerli olacak.
                SigningCredentials = creds,
                Issuer = _config["JwtSettings:Issuer"],
                Audience = _config["JwtSettings:Audience"]
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);

            // Üretilen Token nesnesini string (metin) formatına dönüştürüp dönüyoruz.
            return tokenHandler.WriteToken(token);
        }
    }
}
