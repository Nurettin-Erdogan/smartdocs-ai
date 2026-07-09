using System;
using System.Collections.Generic;

namespace SmartDocsAI.API.DTOs
{
    public class ChatHistoryDto
    {
        public int ConversationId { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<ChatHistoryMessageDto> Messages { get; set; } = new();
    }

    public class ChatHistoryMessageDto
    {
        public int Id { get; set; }
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}