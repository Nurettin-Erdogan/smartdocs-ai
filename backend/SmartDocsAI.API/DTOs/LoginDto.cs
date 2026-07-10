using System.ComponentModel.DataAnnotations;

namespace SmartDocsAI.API.DTOs
{
    public class LoginDto
    {
        [Required(ErrorMessage = "E-posta alanı zorunludur.")]
        [EmailAddress(ErrorMessage = "Geçersiz e-posta adresi.")]
        [StringLength(150, ErrorMessage = "E-posta en fazla 150 karakter olabilir.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Şifre alanı zorunludur.")]
        [StringLength(128, ErrorMessage = "Şifre en fazla 128 karakter olabilir.")]
        public string Password { get; set; } = string.Empty;
    }
}
