// Sistemdeki kullanıcıları temsil eden Entity'dir.
// PostgreSQL'deki Users tablosuna karşılık gelir.
// Kullanıcı bilgileri ve ilişkileri burada tutulur.
//Kullanıcı bilgilerini tutar.
//Kullanıcının rolünü bilir.
//Kullanıcının yüklediği belgeleri bilir.
//Kullanıcının sohbetlerini bilir.


using System.ComponentModel.DataAnnotations;

namespace SmartDocsAI.API.Models
{
    public class User
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [MaxLength(150)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        public int RoleId { get; set; }
        
        // Navigation Properties (İlişkili Tablolar)
        public Role? Role { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Document> Documents { get; set; } = new List<Document>();
        public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    }
}
