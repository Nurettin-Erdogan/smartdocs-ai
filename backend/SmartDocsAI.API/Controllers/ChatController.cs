using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.DTOs;
using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Models;
using System.Security.Claims;

namespace SmartDocsAI.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/chat")]
    public class ChatController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IOllamaService _ollamaService;
        private readonly IQdrantService _qdrantService;

        public ChatController(AppDbContext context, IOllamaService ollamaService, IQdrantService qdrantService)
        {
            _context = context;
            _ollamaService = ollamaService;
            _qdrantService = qdrantService;
        }

        [HttpPost]
        public async Task<IActionResult> Ask([FromBody] ChatRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userIdClaim))
            {
                return Unauthorized(new { Message = "Kullanıcı oturumu geçersiz." });
            }

            var userId = int.Parse(userIdClaim);
            var question = request.Question.Trim();

            var userDocumentIds = await _context.Documents
                .Where(d => d.UserId == userId)
                .Select(d => d.Id)
                .ToListAsync();

            if (userDocumentIds.Count == 0)
            {
                return BadRequest(new { Message = "Önce bir PDF yüklemelisin." });
            }

            var questionEmbedding = await _ollamaService.GetEmbeddingAsync(question);
            var relevantChunks = await _qdrantService.SearchSimilarChunksAsync(questionEmbedding, 3, userDocumentIds);

            if (relevantChunks.Count == 0)
            {
                return NotFound(new { Message = "Soru için ilgili içerik bulunamadı." });
            }

            var contextText = string.Join("\n\n", relevantChunks.Select(chunk =>
                $"[Belge {chunk.DocumentId}, Sayfa {chunk.PageNumber}, Parça {chunk.ChunkIndex}] {chunk.Content}"));

            var prompt = $@"Sen SmartDocs AI asistanısın. Aşağıdaki belge parçalarına dayanarak sadece Türkçe cevap ver.
Eğer cevap belgelerde yoksa bunu açıkça söyle.
Kısa, net ve kaynaklı cevap ver.

Soru:
{question}

Bağlam:
{contextText}";

            var answer = await _ollamaService.GenerateAnswerAsync(prompt);

            Conversation conversation;
            if (request.ConversationId.HasValue)
            {
                conversation = await _context.Conversations
                    .FirstOrDefaultAsync(c => c.Id == request.ConversationId.Value && c.UserId == userId)
                    ?? new Conversation { UserId = userId };

                if (conversation.Id == 0)
                {
                    _context.Conversations.Add(conversation);
                    await _context.SaveChangesAsync();
                }
            }
            else
            {
                conversation = new Conversation { UserId = userId };
                _context.Conversations.Add(conversation);
                await _context.SaveChangesAsync();
            }

            _context.Messages.Add(new Message
            {
                ConversationId = conversation.Id,
                Question = question,
                Answer = answer,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            return Ok(new
            {
                ConversationId = conversation.Id,
                Answer = answer,
                Sources = relevantChunks.Select(chunk => new
                {
                    chunk.DocumentId,
                    chunk.ChunkIndex,
                    chunk.PageNumber,
                    chunk.Score,
                    chunk.Content
                })
            });
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userIdClaim))
            {
                return Unauthorized(new { Message = "Kullanıcı oturumu geçersiz." });
            }

            var userId = int.Parse(userIdClaim);

            var history = await _context.Conversations
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new ChatHistoryDto
                {
                    ConversationId = c.Id,
                    CreatedAt = c.CreatedAt,
                    Messages = c.Messages
                        .OrderBy(m => m.CreatedAt)
                        .Select(m => new ChatHistoryMessageDto
                        {
                            Id = m.Id,
                            Question = m.Question,
                            Answer = m.Answer,
                            CreatedAt = m.CreatedAt
                        })
                        .ToList()
                })
                .ToListAsync();

            return Ok(history);
        }

        [HttpGet("{conversationId}")]
        public async Task<IActionResult> GetConversation(int conversationId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userIdClaim))
            {
                return Unauthorized(new { Message = "Kullanıcı oturumu geçersiz." });
            }

            var userId = int.Parse(userIdClaim);

            var conversation = await _context.Conversations
                .Where(c => c.Id == conversationId && c.UserId == userId)
                .Select(c => new ChatHistoryDto
                {
                    ConversationId = c.Id,
                    CreatedAt = c.CreatedAt,
                    Messages = c.Messages
                        .OrderBy(m => m.CreatedAt)
                        .Select(m => new ChatHistoryMessageDto
                        {
                            Id = m.Id,
                            Question = m.Question,
                            Answer = m.Answer,
                            CreatedAt = m.CreatedAt
                        })
                        .ToList()
                })
                .FirstOrDefaultAsync();

            if (conversation == null)
            {
                return NotFound(new { Message = "Sohbet bulunamadı." });
            }

            return Ok(conversation);
        }
    }
}