using System.ComponentModel.DataAnnotations;

namespace SmartDocsAI.API.DTOs
{
    public class ChatRequestDto
    {
        [Required(ErrorMessage = "Soru alanı zorunludur.")]
        public string Question { get; set; } = string.Empty;

        public int? ConversationId { get; set; }
    }
}