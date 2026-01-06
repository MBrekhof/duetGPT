using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using Anthropic.SDK.Common;
using duetGPT.Data;
using Microsoft.EntityFrameworkCore;
using Markdig;

namespace duetGPT.Services
{
  public interface IChatMessageService
  {
    Task<SendMessageResult> SendMessageAsync(SendMessageRequest request);
    Task<string> GenerateThreadTitleAsync(string userMsg, string assistantMsg, string model);
  }

  public record SendMessageRequest
  {
    public required string UserInput { get; init; }
    public required DuetThread Thread { get; init; }
    public required string Model { get; init; }
    public required string SystemPrompt { get; init; }
    public IEnumerable<int> SelectedFileIds { get; init; } = [];
    public byte[]? ImageBytes { get; init; }
    public string? ImageType { get; init; }
    public bool EnableWebSearch { get; init; }
    public bool EnableExtendedThinking { get; init; }
    public List<Message> ChatHistory { get; init; } = new();
  }

  public record SendMessageResult
  {
    public required string AssistantResponse { get; init; }
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
    public required decimal InputCost { get; init; }
    public required decimal OutputCost { get; init; }
    public string? ThinkingContent { get; init; }
    public required DuetMessage UserMessage { get; init; }
    public required DuetMessage AssistantMessage { get; init; }
  }

  public class ChatMessageService : IChatMessageService
  {
    private readonly AnthropicService _anthropicService;
    private readonly IKnowledgeService _knowledgeService;
    private readonly IThreadService _threadService;
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<ChatMessageService> _logger;

    private record struct ModelCosts(decimal InputRate, decimal OutputRate);

    private static readonly Dictionary<string, ModelCosts> MODEL_COSTS = new()
    {
        { "claude-haiku-4-5-20251001", new ModelCosts(0.001m, 0.005m) },      // Claude Haiku 4.5: $1/$5 per MTok
        { "claude-sonnet-4-5-20250929", new ModelCosts(0.003m, 0.015m) },     // Claude Sonnet 4.5: $3/$15 per MTok
        { "claude-opus-4-1-20250805", new ModelCosts(0.015m, 0.075m) }        // Claude Opus 4.1: $15/$75 per MTok
    };

    public ChatMessageService(
        AnthropicService anthropicService,
        IKnowledgeService knowledgeService,
        IThreadService threadService,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        ILogger<ChatMessageService> logger)
    {
      _anthropicService = anthropicService;
      _knowledgeService = knowledgeService;
      _threadService = threadService;
      _dbContextFactory = dbContextFactory;
      _logger = logger;
    }

    public async Task<SendMessageResult> SendMessageAsync(SendMessageRequest request)
    {
      if (request == null) throw new ArgumentNullException(nameof(request));
      if (string.IsNullOrWhiteSpace(request.UserInput)) throw new ArgumentException("User input cannot be empty", nameof(request));
      if (request.Thread == null) throw new ArgumentNullException(nameof(request.Thread));

      await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

      _logger.LogInformation("Sending message using model: {Model}", request.Model);

      // Create and save user message
      var userMessage = new DuetMessage
      {
        ThreadId = request.Thread.Id,
        Role = "user",
        Content = request.UserInput,
        TokenCount = 0, // Will be updated after response
        MessageCost = 0 // Will be updated after response
      };
      dbContext.Add(userMessage);
      await dbContext.SaveChangesAsync();

      // Get relevant knowledge from RAG
      List<string> knowledgeContent = new List<string>();
      try
      {
        var relevantKnowledge = await _knowledgeService.GetRelevantKnowledgeAsync(request.UserInput);
        if (relevantKnowledge != null && relevantKnowledge.Any())
        {
          knowledgeContent.AddRange(relevantKnowledge.Select(k => k.Content));
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error retrieving relevant knowledge");
      }

      // Add document content if files are selected
      if (request.SelectedFileIds != null && request.SelectedFileIds.Any())
      {
        await _threadService.AssociateDocumentsWithThreadAsync(request.Thread, request.SelectedFileIds);
        var threadDocs = await _threadService.GetThreadDocumentContentsAsync(request.Thread);
        if (threadDocs != null && threadDocs.Any())
        {
          knowledgeContent.AddRange(threadDocs);
        }
      }

      // Build system prompt with knowledge
      string systemPrompt = request.SystemPrompt;
      if (knowledgeContent.Any())
      {
        systemPrompt += "\n\nRelevant knowledge base content:\n" + string.Join("\n---\n", knowledgeContent);
      }

      // Create message with image if available
      Message message;
      if (request.ImageBytes != null && request.ImageType != null)
      {
        string base64Data = Convert.ToBase64String(request.ImageBytes);
        message = new Message
        {
          Role = RoleType.User,
          Content = new List<ContentBase>
                {
                    new ImageContent
                    {
                        Source = new ImageSource
                        {
                            MediaType = request.ImageType,
                            Data = base64Data
                        }
                    },
                    new Anthropic.SDK.Messaging.TextContent
                    {
                        Text = request.UserInput
                    }
                }
        };
      }
      else
      {
        message = new Message(RoleType.User, request.UserInput, null);
      }

      var client = _anthropicService.GetAnthropicClient();
      MessageResponse res;
      string markdown;
      string? thinkingContent = null;

      // Check if extended thinking is enabled and available
      bool useStandardApi = false;
      if (request.EnableExtendedThinking && IsExtendedThinkingAvailable(request.Model))
      {
        _logger.LogInformation("Enabling extended thinking for this request");

        try
        {
          var systemMessages = new List<SystemMessage>()
                {
                    new SystemMessage(systemPrompt, new CacheControl() { Type = CacheControlType.ephemeral })
                };

          var tools = Anthropic.SDK.Common.Tool.GetAllAvailableTools(includeDefaults: false,
                            forceUpdate: true, clearCache: true);

          var extendedRequest = new MessageParameters()
          {
            Messages = request.ChatHistory.Concat(new[] { message }).ToList(),
            Model = request.Model,
            Stream = false,
            MaxTokens = 20000,
            Temperature = 1.0m,
            System = systemMessages,
            Thinking = new Anthropic.SDK.Messaging.ThinkingParameters()
            {
              BudgetTokens = 16000
            },
            Tools = tools.ToList(),
          };

          if (request.EnableWebSearch)
          {
            if (extendedRequest.Tools == null) extendedRequest.Tools = new List<Anthropic.SDK.Common.Tool>();
            var webSearchTool = ServerTools.GetWebSearchTool(5, null, new List<string>());
            if (!extendedRequest.Tools.Any(t => t.GetType() == webSearchTool.GetType()))
            {
              extendedRequest.Tools.Add(webSearchTool);
            }
            extendedRequest.ToolChoice = new ToolChoice() { Type = ToolChoiceType.Auto };
          }

          var extendedResponse = await client.Messages.GetClaudeMessageAsync(extendedRequest);
          res = extendedResponse;

          markdown = res.Message?.ToString() ?? "No message content in extended response.";

          var receivedThinkingContent = res.Message.ThinkingContent;
          if (!string.IsNullOrEmpty(receivedThinkingContent))
          {
            thinkingContent = receivedThinkingContent;
            _logger.LogInformation("Received thinking content: {Length} characters", thinkingContent.Length);
          }
          else
          {
            _logger.LogWarning("No thinking content was found in the response");
            thinkingContent = "Extended thinking was requested but not returned by the model. This may be due to API limitations or the specific query type.";
          }
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Error using extended thinking feature. Falling back to standard API.");
          useStandardApi = true;
        }
      }
      else
      {
        useStandardApi = true;
      }

      if (useStandardApi)
      {
        _logger.LogInformation("Using standard API path.");

        var systemMessages = new List<SystemMessage>()
            {
                new SystemMessage(systemPrompt, new CacheControl() { Type = CacheControlType.ephemeral })
            };

        var tools = Anthropic.SDK.Common.Tool.GetAllAvailableTools(includeDefaults: false,
                            forceUpdate: true, clearCache: true);

        var apiCallMessages = new List<Message>(request.ChatHistory);
        apiCallMessages.Add(message);

        var parameters = new MessageParameters
        {
          Messages = apiCallMessages,
          Model = request.Model,
          MaxTokens = 16384,
          Stream = false,
          Temperature = 1.0m,
          System = systemMessages,
          Tools = tools.ToList(),
          ToolChoice = new ToolChoice { Type = ToolChoiceType.Auto },
        };

        if (request.EnableWebSearch)
        {
          var webSearchTool = ServerTools.GetWebSearchTool(5, null, new List<string>());
          if (!parameters.Tools.Any(t => t.GetType() == webSearchTool.GetType()))
          {
            parameters.Tools.Add(webSearchTool);
            _logger.LogInformation("Web search tool added to tools list for this request");
          }
        }

        _logger.LogInformation("Using non-streaming API call. Initial messages count: {Count}", parameters.Messages.Count);
        res = await client.Messages.GetClaudeMessageAsync(parameters);
        _logger.LogInformation("First API call completed. Stop Reason: {StopReason}", res.StopReason);

        // Handle tool calls if present
        if (res.ToolCalls != null && res.ToolCalls.Any())
        {
          _logger.LogInformation("Tool calls received: {Count}", res.ToolCalls.Count);

          // Add assistant message with tool calls to history
          var chatMessages = new List<Message>(apiCallMessages);
          if (res.Message != null)
          {
            chatMessages.Add(res.Message);
          }

          foreach (var toolCall in res.ToolCalls)
          {
            _logger.LogInformation("Invoking tool: {ToolName}, ID: {ToolId}", toolCall.Name, toolCall.Id);
            var toolResponseContent = await toolCall.InvokeAsync<string>();
            var toolMessage = new Message(toolCall, toolResponseContent);
            chatMessages.Add(toolMessage);
            _logger.LogInformation("Tool {ToolName} (ID: {ToolId}) invoked, response length: {Length}",
                toolCall.Name, toolCall.Id, toolResponseContent?.Length ?? 0);
          }

          var finalParameters = new MessageParameters
          {
            Messages = chatMessages,
            Model = parameters.Model,
            MaxTokens = parameters.MaxTokens,
            Stream = false,
            Temperature = parameters.Temperature,
            System = parameters.System,
            Tools = parameters.Tools,
            ToolChoice = parameters.ToolChoice
          };

          _logger.LogInformation("Making second API call with {Count} messages after tool results.", chatMessages.Count);
          var finalResult = await client.Messages.GetClaudeMessageAsync(finalParameters);
          _logger.LogInformation("Second API call completed. Stop Reason: {StopReason}", finalResult.StopReason);

          markdown = finalResult.Message?.ToString() ?? "No message content in final response after tool use.";
          res = finalResult;
        }
        else
        {
          _logger.LogInformation("No tool calls in the first response.");
          markdown = res.Message?.ToString() ?? "No message content in response.";
        }
      }

      // Calculate costs
      if (res == null || res.Usage == null)
      {
        _logger.LogError("API response or usage information is null. Cannot calculate tokens or cost accurately.");
        throw new InvalidOperationException("API response or usage information is null");
      }

      var costs = GetModelCosts(request.Model);
      decimal inputCost = CalculateCost(res.Usage.InputTokens, costs.InputRate);
      decimal outputCost = CalculateCost(res.Usage.OutputTokens, costs.OutputRate);

      // Update user message with token count and cost
      userMessage.TokenCount = res.Usage.InputTokens;
      userMessage.MessageCost = inputCost;
      await dbContext.SaveChangesAsync();

      // Create and save assistant message
      var assistantMessage = new DuetMessage
      {
        ThreadId = request.Thread.Id,
        Role = "assistant",
        Content = markdown,
        TokenCount = res.Usage.OutputTokens,
        MessageCost = outputCost
      };
      dbContext.Add(assistantMessage);
      await dbContext.SaveChangesAsync();

      _logger.LogInformation("Message sent and processed successfully");

      return new SendMessageResult
      {
        AssistantResponse = markdown,
        InputTokens = res.Usage.InputTokens,
        OutputTokens = res.Usage.OutputTokens,
        InputCost = inputCost,
        OutputCost = outputCost,
        ThinkingContent = thinkingContent,
        UserMessage = userMessage,
        AssistantMessage = assistantMessage
      };
    }

    public async Task<string> GenerateThreadTitleAsync(string userMsg, string assistantMsg, string model)
    {
      if (string.IsNullOrEmpty(userMsg)) throw new ArgumentException("User message cannot be empty", nameof(userMsg));
      if (string.IsNullOrEmpty(assistantMsg)) throw new ArgumentException("Assistant message cannot be empty", nameof(assistantMsg));

      try
      {
        var client = _anthropicService.GetAnthropicClient();

        var titlePrompt = new Message(
            RoleType.User,
            $"Based on this conversation, generate a concise and descriptive title (maximum 100 characters):\n\nUser: {userMsg}\n\nAssistant: {assistantMsg}",
            null
        );

        var titleParameters = new MessageParameters()
        {
          Messages = new List<Message> { titlePrompt },
          Model = model,
          MaxTokens = 100,
          Stream = false,
          Temperature = 0.7m,
          System = new List<SystemMessage>
                {
                    new SystemMessage(
                        "Generate a concise, descriptive title that captures the main topic or purpose of the conversation. The title should be clear and informative, but not exceed 100 characters.",
                        new CacheControl() { Type = CacheControlType.ephemeral }
                    )
                },
        };

        var titleResponse = await client.Messages.GetClaudeMessageAsync(titleParameters);
        var generatedTitle = titleResponse.Message?.ToString()?.Trim('"', ' ', '\n') ?? "Chat Title";

        if (generatedTitle.Length > 100)
        {
          generatedTitle = generatedTitle.Substring(0, 97) + "...";
        }

        _logger.LogInformation("Thread title generated: {Title}", generatedTitle);
        return generatedTitle;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error generating thread title");
        return "Chat Title";
      }
    }

    private bool IsExtendedThinkingAvailable(string model)
    {
      // Extended thinking is only available for Claude 3.7 Sonnet (and potentially newer models)
      return model.Contains("sonnet", StringComparison.OrdinalIgnoreCase) ||
             model.Contains("opus", StringComparison.OrdinalIgnoreCase);
    }

    private ModelCosts GetModelCosts(string model)
    {
      return MODEL_COSTS.TryGetValue(model, out var costs) ? costs : MODEL_COSTS["claude-sonnet-4-5-20250929"];
    }

    private static decimal CalculateCost(int tokens, decimal ratePerMTok)
    {
      try
      {
        return (tokens / 1_000_000m) * ratePerMTok;
      }
      catch (Exception)
      {
        return 0;
      }
    }
  }
}
