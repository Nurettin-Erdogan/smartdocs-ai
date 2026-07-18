using System.ComponentModel.DataAnnotations;

namespace SmartDocsAI.API.DTOs
{
    public class ChatRequestDto
    {
        [Required(ErrorMessage = "Soru alanı zorunludur.")]
        [StringLength(2000, MinimumLength = 1, ErrorMessage = "Soru 1-2000 karakter arasında olmalıdır.")]
        public string Question { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir sohbet seçilmelidir.")]
        public int? ConversationId { get; set; }

        [MaxLength(50, ErrorMessage = "Bir sohbette en fazla 50 belge seçilebilir.")]
        public List<int>? DocumentIds { get; set; }
    }
}
