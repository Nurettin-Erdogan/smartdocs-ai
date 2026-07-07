using System.ComponentModel.DataAnnotations;

namespace SmartDocsAI.API.Models
{
    public class Chunk
    {
        public int Id { get; set; }

        public int DocumentId { get; set; }
        public Document? Document { get; set; }

        public int ChunkIndex { get; set; }

        [Required]
        public string Content { get; set; } = string.Empty;

        public int PageNumber { get; set; }
    }
}
