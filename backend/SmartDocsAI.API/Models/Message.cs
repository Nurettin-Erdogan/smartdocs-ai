// Sohbet içerisindeki mesajları temsil eden Entity'dir.
// PostgreSQL'deki Messages tablosuna karşılık gelir.
// Kullanıcı ve yapay zekanın mesajları burada tutulur.
//Conversation'ın içindeki mesajları tutar.

using System.ComponentModel.DataAnnotations;

namespace SmartDocsAI.API.Models
{
    public class Message
    {
        public int Id { get; set; }

        public int ConversationId { get; set; }
        public Conversation? Conversation { get; set; }

        [Required]
        public string Question { get; set; } = string.Empty;

        [Required]
        public string Answer { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
