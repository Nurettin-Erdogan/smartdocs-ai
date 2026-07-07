using SmartDocsAI.API.Models;

namespace SmartDocsAI.API.Interfaces
{
    public interface ITokenService
    {
        /// <summary>
        /// Giriş yapan kullanıcı için JWT (JSON Web Token) üretir.
        /// </summary>
        string CreateToken(User user);
    }
}
