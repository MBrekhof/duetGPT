using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using duetGPT.Data;
using duetGPT.Services;
using System.Security.Claims;
using Anthropic.SDK.Messaging;

namespace duetGPT.Controllers
{
  [ApiController]
  [Route("api/[controller]")]
  [Authorize]
  public class ChatController : ControllerBase
  {
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly IChatMessageService _chatMessageService;
    private readonly IThreadService _threadService;
    private readonly IKnowledgeService _knowledgeService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IChatMessageService chatMessageService,
        IThreadService threadService,
        IKnowledgeService knowledgeService,
        ILogger<ChatController> logger)
    {
      _dbContextFactory = dbContextFactory;
      _chatMessageService = chatMessageService;
      _threadService = threadService;
      _knowledgeService = knowledgeService;
      _logger = logger;
    }

    private string GetUserId()
    {
      return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
          ?? throw new UnauthorizedAccessException("User ID not found");
    }

    // POST: api/chat
    [HttpPost]
    public async Task<ActionResult<ChatResponseDto>> SendMessage([FromBody] ChatRequestDto request)
    {
      try
      {
        var userId = GetUserId();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        // Get or create thread
        DuetThread thread;
        if (request.ThreadId.HasValue)
        {
          thread = await dbContext.Threads
              .FirstOrDefaultAsync(t => t.Id == request.ThreadId.Value && t.UserId == userId);

          if (thread == null)
            return NotFound(new { message = "Thread not found" });
        }
        else
        {
          // Create new thread
          thread = await _threadService.CreateThreadAsync(userId);
        }

        // Load chat history
        var chatHistory = await _threadService.LoadThreadMessagesAsync(thread.Id);

        // Build system prompt
        var systemPrompt = "You are a helpful assistant.";

        if (!string.IsNullOrEmpty(request.CustomPrompt))
        {
          systemPrompt = request.CustomPrompt;
        }

        // Add RAG context if enabled
        if (request.EnableRag)
        {
          var ragResults = await _knowledgeService.GetRelevantKnowledgeAsync(request.Message);
          if (ragResults.Any())
          {
            var ragContext = string.Join("\n\n", ragResults.Select(k => k.Content));
            systemPrompt += $"\n\nRelevant context from knowledge base:\n{ragContext}";
          }
        }

        // Prepare send message request
        var sendRequest = new SendMessageRequest
        {
          UserInput = request.Message,
          Thread = thread,
          Model = request.Model,
          SystemPrompt = systemPrompt,
          ChatHistory = chatHistory,
          EnableWebSearch = request.EnableWebSearch,
          EnableExtendedThinking = request.EnableExtendedThinking,
          SelectedFileIds = request.AttachedDocumentIds ?? []
        };

        // Handle image if provided
        if (!string.IsNullOrEmpty(request.ImageData))
        {
          // ImageData format: "data:image/png;base64,..."
          var parts = request.ImageData.Split(',');
          if (parts.Length == 2)
          {
            var base64Data = parts[1];
            var imageBytes = Convert.FromBase64String(base64Data);
            var imageType = parts[0].Contains("png") ? "image/png" : "image/jpeg";

            sendRequest = sendRequest with
            {
              ImageBytes = imageBytes,
              ImageType = imageType
            };
          }
        }

        // Send message
        var result = await _chatMessageService.SendMessageAsync(sendRequest);

        // Update thread metrics
        var totalTokens = result.InputTokens + result.OutputTokens;
        var totalCost = result.InputCost + result.OutputCost;
        await _threadService.UpdateThreadMetricsAsync(thread, totalTokens, totalCost);

        // Generate thread title if this is the first message
        if (chatHistory.Count == 0)
        {
          try
          {
            var title = await _chatMessageService.GenerateThreadTitleAsync(
                request.Message,
                result.AssistantResponse,
                request.Model);

            var dbThread = await dbContext.Threads.FindAsync(thread.Id);
            if (dbThread != null)
            {
              dbThread.Title = title;
              await dbContext.SaveChangesAsync();
            }
          }
          catch (Exception ex)
          {
            _logger.LogWarning(ex, "Failed to generate thread title");
          }
        }

        var response = new ChatResponseDto
        {
          Content = result.AssistantResponse,
          Thinking = result.ThinkingContent,
          ThreadId = thread.Id,
          MessageId = result.AssistantMessage.Id,
          Tokens = totalTokens,
          Cost = totalCost
        };

        return Ok(response);
      }
      catch (UnauthorizedAccessException ex)
      {
        return Unauthorized(new { message = ex.Message });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error sending message");
        // Return detailed error in development for debugging
        return StatusCode(500, new { message = "An error occurred while sending the message", error = ex.Message, innerError = ex.InnerException?.Message });
      }
    }
  }

  // DTOs
  public class ChatRequestDto
  {
    public int? ThreadId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-3-5-sonnet-20241022";
    public bool EnableRag { get; set; }
    public bool EnableExtendedThinking { get; set; }
    public bool EnableWebSearch { get; set; }
    public string? CustomPrompt { get; set; }
    public List<int>? AttachedDocumentIds { get; set; }
    public string? ImageData { get; set; }
  }

  public class ChatResponseDto
  {
    public string Content { get; set; } = string.Empty;
    public string? Thinking { get; set; }
    public int ThreadId { get; set; }
    public int MessageId { get; set; }
    public int Tokens { get; set; }
    public decimal Cost { get; set; }
  }
}
