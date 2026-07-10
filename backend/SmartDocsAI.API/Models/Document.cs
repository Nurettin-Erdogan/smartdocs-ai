// Kullanıcıların yüklediği PDF belgelerini temsil eden Entity'dir.
// PostgreSQL'deki Documents tablosuna karşılık gelir.
// Belgeye ait bilgiler burada tutulur.
//PDF'nin kendisini temsil eder.
//Kim yükledi?
//Ne zaman yükledi?
//Dosyanın adı ne?
//Dosya nerede?

using System.ComponentModel.DataAnnotations;

namespace SmartDocsAI.API.Models
{
    public class Document
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User? User { get; set; }

        [Required]
        [MaxLength(255)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string FileType { get; set; } = string.Empty;

        [Required]
        public string FilePath { get; set; } = string.Empty;

        public DateTime UploadDate { get; set; }

        public long FileSize { get; set; }

        public ICollection<Chunk> Chunks { get; set; } = new List<Chunk>();
    }
}