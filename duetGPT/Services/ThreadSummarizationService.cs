using Anthropic.SDK.Messaging;
using duetGPT.Data;

namespace duetGPT.Services
{
  public interface IThreadSummarizationService
  {
    Task<SummarizationResult> SummarizeAndSaveAsync(
        DuetThread thread, List<Message> chatMessages, string model, string userId);
  }

  public record SummarizationResult
  {
    public required string Summary { get; init; }
    public required Knowledge SavedKnowledge { get; init; }
  }

  public class ThreadSummarizationService : IThreadSummarizationService
  {
    private readonly AnthropicService _anthropicService;
    private readonly IKnowledgeService _knowledgeService;
    private readonly ILogger<ThreadSummarizationService> _logger;

    public ThreadSummarizationService(
        AnthropicService anthropicService,
        IKnowledgeService knowledgeService,
        ILogger<ThreadSummarizationService> logger)
    {
      _anthropicService = anthropicService;
      _knowledgeService = knowledgeService;
      _logger = logger;
    }

    public async Task<SummarizationResult> SummarizeAndSaveAsync(
        DuetThread thread, List<Message> chatMessages, string model, string userId)
    {
      if (thread == null)
      {
        throw new ArgumentNullException(nameof(thread), "Thread cannot be null");
      }

      if (!chatMessages.Any())
      {
        throw new ArgumentException("No messages to summarize", nameof(chatMessages));
      }

      if (string.IsNullOrEmpty(userId))
      {
        throw new ArgumentException("User ID is required", nameof(userId));
      }

      try
      {
        _logger.LogInformation("Summarizing thread {ThreadId}", thread.Id);

        // Create prompt for summarization
        var threadContent = string.Join("\n\n", chatMessages.Select(m =>
        {
          var content = m.Content is List<ContentBase> contentList
              ? string.Join(" ", contentList.OfType<TextContent>().Select(tc => tc.Text))
              : m.Content?.ToString() ?? "";
          return $"{m.Role}: {content}";
        }));

        var summarizationPrompt = $"Please provide a concise summary of the following conversation, highlighting the key points and conclusions:\n\n{threadContent}";

        // Get summary from Claude
        var client = _anthropicService.GetAnthropicClient();
        var messages = new List<Message>
        {
            new Message(RoleType.User, summarizationPrompt)
        };

        var parameters = new MessageParameters
        {
          Messages = messages,
          MaxTokens = 1024,
          Model = model,
          Stream = false,
          Temperature = 0.7m
        };

        var response = await client.Messages.GetClaudeMessageAsync(parameters);
        var summary = response.Content[0].ToString();

        // Save to knowledge base
        var metadata = $"type:chat_summary;source:thread_{thread.Id};date:{DateTime.UtcNow:yyyy-MM-dd}";

        // Ensure title stays within 50 character limit
        var baseTitle = thread.Title ?? $"Thread {thread.Id}";
        var title = $"Summary - {baseTitle}";
        if (title.Length > 50)
        {
          // Truncate the base title to fit within limits, accounting for "Chat Summary - " (14 chars) and ellipsis (3 chars)
          var maxBaseTitleLength = 42;
          title = $"Chat - {baseTitle.Substring(0, Math.Min(maxBaseTitleLength, baseTitle.Length))}";
        }

        var savedKnowledge = await _knowledgeService.SaveKnowledgeAsync(summary, title, metadata, userId);

        _logger.LogInformation("Thread {ThreadId} summary saved to knowledge base with ID {KnowledgeId}",
            thread.Id, savedKnowledge.RagDataId);

        return new SummarizationResult
        {
          Summary = summary,
          SavedKnowledge = savedKnowledge
        };
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error summarizing thread {ThreadId}", thread.Id);
        throw;
      }
    }
  }
}
