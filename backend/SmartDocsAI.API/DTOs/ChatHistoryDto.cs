namespace SmartDocsAI.API.DTOs;

public sealed class ChatHistorySummaryDto
{
    public int ConversationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string FirstQuestion { get; set; } = string.Empty;
    public int MessageCount { get; set; }
}

public sealed class ChatHistoryDto
{
    public int ConversationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<ChatHistoryMessageDto> Messages { get; set; } = new();
}

public sealed class ChatHistoryMessageDto
{
    public int Id { get; set; }
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
