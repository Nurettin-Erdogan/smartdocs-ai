using System.ComponentModel.DataAnnotations;

namespace SmartDocsAI.API.Models
{
    public class Role
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Name { get; set; } = string.Empty;

        // Navigation Property (İlişkili Kullanıcılar)
        public ICollection<User> Users { get; set; } = new List<User>();
    }
}
