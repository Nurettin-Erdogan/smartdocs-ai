using System.ComponentModel.DataAnnotations;

namespace SmartDocsAI.API.DTOs
{
    public class RegisterDto
    {
        [Required(ErrorMessage = "Ad Soyad alanı zorunludur.")]
        [StringLength(100, ErrorMessage = "Ad Soyad en fazla 100 karakter olabilir.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "E-posta alanı zorunludur.")]
        [EmailAddress(ErrorMessage = "Geçersiz e-posta adresi.")]
        [StringLength(150, ErrorMessage = "E-posta en fazla 150 karakter olabilir.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Şifre alanı zorunludur.")]
        [StringLength(128, MinimumLength = 8, ErrorMessage = "Şifre 8-128 karakter arasında olmalıdır.")]
        public string Password { get; set; } = string.Empty;
    }
}
