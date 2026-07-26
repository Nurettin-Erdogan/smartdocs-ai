using System;

namespace SmartDocsAI.API.DTOs
{
    public class DocumentDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime UploadDate { get; set; }
        public string IndexingStatus { get; set; } = string.Empty;
        public string? IndexingError { get; set; }
    }
}
