namespace SmartDocsAI.API.Helpers
{
    public static class PasswordHasher
    {
        /// <summary>
        /// Şifreyi tek yönlü olarak hashler (özetler).
        /// </summary>
        public static string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        /// <summary>
        /// Girilen şifrenin, veritabanındaki hash ile eşleşip eşleşmediğini doğrular.
        /// </summary>
        public static bool VerifyPassword(string password, string hashedPassword)
        {
            return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
        }
    }
}
