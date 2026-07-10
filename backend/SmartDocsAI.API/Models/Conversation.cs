// Kullanıcı ile yapay zeka arasındaki sohbetleri temsil eden Entity'dir.
// PostgreSQL'deki Conversations tablosuna karşılık gelir.
// Sohbet bilgileri burada tutulur.
//Bir sohbet oluşturur.

namespace SmartDocsAI.API.Models
{
    public class Conversation
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User? User { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Property (İlişkili Mesajlar)
        public ICollection<Message> Messages { get; set; } = new List<Message>();
    }
}
