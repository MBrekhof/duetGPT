using Microsoft.Extensions.AI;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Common;
using duetGPT.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime.CompilerServices;

namespace duetGPT.Services
{
  /// <summary>
  /// Adapter that wraps AnthropicService to implement Microsoft.Extensions.AI.IChatClient interface
  /// for DevExpress DxAIChat component integration
  /// </summary>
  public class AnthropicChatClientAdapter : IChatClient
  {
    private readonly AnthropicService _anthropicService;
    private readonly IKnowledgeService _knowledgeService;
    private readonly IThreadService _threadService;
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<AnthropicChatClientAdapter> _logger;
    private readonly IChatContextService _chatContext;
    private readonly string _defaultModelId;

    public AnthropicChatClientAdapter(
        AnthropicService anthropicService,
        IKnowledgeService knowledgeService,
        IThreadService threadService,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        ILogger<AnthropicChatClientAdapter> logger,
        IChatContextService chatContext,
        string modelId = "claude-sonnet-4-5-20250929")
    {
      _anthropicService = anthropicService;
      _knowledgeService = knowledgeService;
      _threadService = threadService;
      _dbContextFactory = dbContextFactory;
      _logger = logger;
      _chatContext = chatContext;
      _defaultModelId = modelId;
    }

    public ChatClientMetadata Metadata => new("Anthropic", new Uri("https://www.anthropic.com"), _defaultModelId);

    public TService? GetService<TService>(object? key = null) where TService : class
    {
      return this as TService;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
      return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
      // No resources to dispose
    }

    public async Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var chatMessagesList = chatMessages.ToList();
        _logger.LogWarning("=== GetResponseAsync CALLED === Message count: {Count}", chatMessagesList.Count);

        // Extract context from options
        var context = ExtractContext(options);
        var modelId = _chatContext.ModelId ?? _defaultModelId;
        _logger.LogWarning("Context: ThreadId={ThreadId}, EnableRag={EnableRag}, EnableExtendedThinking={EnableExtendedThinking}, ModelId={ModelId}",
            context.ThreadId, context.EnableRag, context.EnableExtendedThinking, modelId);

        // Get the user's last message
        var lastUserMessage = chatMessagesList.LastOrDefault(m => m.Role == ChatRole.User);
        if (lastUserMessage == null || lastUserMessage.Text == null)
        {
          throw new InvalidOperationException("No user message found");
        }

        // Build system prompt with RAG context
        var systemPrompt = await BuildSystemPromptAsync(lastUserMessage.Text, context);

        // Convert messages to Anthropic format
        var anthropicMessages = ConvertToAnthropicMessages(chatMessagesList);

        var client = _anthropicService.GetAnthropicClient();

        // Get available tools
        var tools = Anthropic.SDK.Common.Tool.GetAllAvailableTools(includeDefaults: false,
                            forceUpdate: true, clearCache: true);

        var parameters = new MessageParameters
        {
          Messages = anthropicMessages,
          Model = modelId,
          MaxTokens = options?.MaxOutputTokens ?? 16384,
          Stream = false,
          Temperature = (decimal?)options?.Temperature ?? 1.0m,
          System = new List<SystemMessage>
                {
                    new SystemMessage(systemPrompt, new CacheControl() { Type = CacheControlType.ephemeral })
                },
          Tools = tools.ToList(),
          ToolChoice = new ToolChoice { Type = ToolChoiceType.Auto }
        };

        // Add web search tool if enabled
        if (context.EnableWebSearch)
        {
          var webSearchTool = ServerTools.GetWebSearchTool(5, null, new List<string>());
          if (!parameters.Tools.Any(t => t.GetType() == webSearchTool.GetType()))
          {
            parameters.Tools.Add(webSearchTool);
          }
        }

        // Add extended thinking if enabled
        if (context.EnableExtendedThinking && IsExtendedThinkingAvailable(modelId))
        {
          parameters.Thinking = new Anthropic.SDK.Messaging.ThinkingParameters()
          {
            BudgetTokens = 16000
          };
        }

        var response = await client.Messages.GetClaudeMessageAsync(parameters, cancellationToken);

        // Handle tool calls if present
        if (response.ToolCalls != null && response.ToolCalls.Any())
        {
          _logger.LogInformation("Tool calls received: {Count}", response.ToolCalls.Count);

          var messagesWithTools = new List<Message>(anthropicMessages);
          if (response.Message != null)
          {
            messagesWithTools.Add(response.Message);
          }

          foreach (var toolCall in response.ToolCalls)
          {
            _logger.LogInformation("Invoking tool: {ToolName}", toolCall.Name);
            var toolResponseContent = await toolCall.InvokeAsync<string>();
            var toolMessage = new Message(toolCall, toolResponseContent);
            messagesWithTools.Add(toolMessage);
          }

          var finalParameters = new MessageParameters
          {
            Messages = messagesWithTools,
            Model = parameters.Model,
            MaxTokens = parameters.MaxTokens,
            Stream = false,
            Temperature = parameters.Temperature,
            System = parameters.System,
            Tools = parameters.Tools,
            ToolChoice = parameters.ToolChoice
          };

          response = await client.Messages.GetClaudeMessageAsync(finalParameters, cancellationToken);
        }

        // Convert response to ChatResponse
        var messageText = response.Message?.ToString() ?? string.Empty;

        var chatResponse = new Microsoft.Extensions.AI.ChatResponse(new[]
        {
                new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, messageText)
        })
        {
          ModelId = modelId,
          FinishReason = MapStopReason(response.StopReason)
        };

        // Add usage information
        if (response.Usage != null)
        {
          chatResponse.Usage = new UsageDetails
          {
            InputTokenCount = response.Usage.InputTokens,
            OutputTokenCount = response.Usage.OutputTokens,
            TotalTokenCount = response.Usage.InputTokens + response.Usage.OutputTokens
          };
        }

        _logger.LogInformation("GetResponseAsync completed successfully");
        return chatResponse;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error in GetResponseAsync");
        throw;
      }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      var chatMessagesList = chatMessages.ToList();
      _logger.LogWarning("=== GetStreamingResponseAsync CALLED === Message count: {Count}", chatMessagesList.Count);

      // Extract context from options
      var context = ExtractContext(options);
      var modelId = _chatContext.ModelId ?? _defaultModelId;
      _logger.LogWarning("Streaming Context: ThreadId={ThreadId}, EnableRag={EnableRag}, ModelId={ModelId}",
          context.ThreadId, context.EnableRag, modelId);

      // Get the user's last message
      var lastUserMessage = chatMessagesList.LastOrDefault(m => m.Role == ChatRole.User);
      if (lastUserMessage == null || lastUserMessage.Text == null)
      {
        throw new InvalidOperationException("No user message found");
      }

      // Build system prompt with RAG context
      var systemPrompt = await BuildSystemPromptAsync(lastUserMessage.Text, context);

      // Convert messages to Anthropic format
      var anthropicMessages = ConvertToAnthropicMessages(chatMessagesList);

      var client = _anthropicService.GetAnthropicClient();

      // Get available tools
      var tools = Anthropic.SDK.Common.Tool.GetAllAvailableTools(includeDefaults: false,
                          forceUpdate: true, clearCache: true);

      var parameters = new MessageParameters
      {
        Messages = anthropicMessages,
        Model = modelId,
        MaxTokens = options?.MaxOutputTokens ?? 16384,
        Stream = true,
        Temperature = (decimal?)options?.Temperature ?? 1.0m,
        System = new List<SystemMessage>
            {
                new SystemMessage(systemPrompt, new CacheControl() { Type = CacheControlType.ephemeral })
            },
        Tools = tools.ToList(),
        ToolChoice = new ToolChoice { Type = ToolChoiceType.Auto }
      };

      // Add web search tool if enabled
      if (context.EnableWebSearch)
      {
        var webSearchTool = ServerTools.GetWebSearchTool(5, null, new List<string>());
        if (!parameters.Tools.Any(t => t.GetType() == webSearchTool.GetType()))
        {
          parameters.Tools.Add(webSearchTool);
        }
      }

      await foreach (var streamRes in client.Messages.StreamClaudeMessageAsync(parameters, cancellationToken))
      {
        if (streamRes.Delta != null && !string.IsNullOrEmpty(streamRes.Delta.Text))
        {
          yield return new ChatResponseUpdate
          {
            Role = ChatRole.Assistant,
            Contents = [new Microsoft.Extensions.AI.TextContent(streamRes.Delta.Text)]
          };
        }
      }
    }

    private RequestContext ExtractContext(ChatOptions? options)
    {
      // Read context from the shared chat context service instead of ChatOptions
      return new RequestContext
      {
        SelectedFiles = _chatContext.SelectedFiles,
        ThreadId = _chatContext.ThreadId,
        CustomPrompt = _chatContext.CustomPrompt,
        EnableRag = _chatContext.EnableRag,
        EnableWebSearch = _chatContext.EnableWebSearch,
        EnableExtendedThinking = _chatContext.EnableExtendedThinking
      };
    }

    private async Task<string> BuildSystemPromptAsync(string userQuery, RequestContext context)
    {
      var prompt = context.CustomPrompt ?? @"You are an expert at analyzing user questions and providing accurate, relevant answers.
Use the following guidelines:
1. Prioritize information from the provided knowledge base when available
2. Supplement with your general knowledge when needed
3. Clearly indicate when you're using provided knowledge versus general knowledge
4. If the provided knowledge seems insufficient or irrelevant, rely on your general expertise
5. Ultrathink and Ultracheck your answer before answering";

      var knowledgeContent = new List<string>();

      // Add RAG context if enabled
      if (context.EnableRag)
      {
        try
        {
          var relevantKnowledge = await _knowledgeService.GetRelevantKnowledgeAsync(userQuery);
          if (relevantKnowledge != null && relevantKnowledge.Any())
          {
            knowledgeContent.AddRange(relevantKnowledge.Select(k => k.Content));
          }
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Error retrieving RAG knowledge");
        }
      }

      // Add document content if files selected
      if (context.SelectedFiles.Any() && context.ThreadId > 0)
      {
        try
        {
          await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
          var thread = await dbContext.Threads
              .Include(t => t.ThreadDocuments)
              .FirstOrDefaultAsync(t => t.Id == context.ThreadId);

          if (thread != null)
          {
            var docs = await _threadService.GetThreadDocumentContentsAsync(thread);
            if (docs != null && docs.Any())
            {
              knowledgeContent.AddRange(docs);
            }
          }
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Error retrieving thread documents");
        }
      }

      if (knowledgeContent.Any())
      {
        prompt += "\n\nRelevant knowledge base content:\n" + string.Join("\n---\n", knowledgeContent);
      }

      return prompt;
    }

    private List<Message> ConvertToAnthropicMessages(IList<Microsoft.Extensions.AI.ChatMessage> chatMessages)
    {
      var anthropicMessages = new List<Message>();

      foreach (var msg in chatMessages)
      {
        // Skip system messages (handled separately in system prompt)
        if (msg.Role == ChatRole.System)
          continue;

        var role = msg.Role == ChatRole.User ? RoleType.User : RoleType.Assistant;
        var content = msg.Text ?? string.Empty;

        anthropicMessages.Add(new Message(role, content, null));
      }

      return anthropicMessages;
    }

    private bool IsExtendedThinkingAvailable(string model)
    {
      return model.Contains("sonnet", StringComparison.OrdinalIgnoreCase) ||
             model.Contains("opus", StringComparison.OrdinalIgnoreCase);
    }

    private ChatFinishReason? MapStopReason(string? stopReason)
    {
      return stopReason switch
      {
        "end_turn" => ChatFinishReason.Stop,
        "max_tokens" => ChatFinishReason.Length,
        "tool_use" => ChatFinishReason.ToolCalls,
        _ => null
      };
    }

    private record RequestContext
    {
      public IEnumerable<int> SelectedFiles { get; init; } = Enumerable.Empty<int>();
      public int ThreadId { get; init; }
      public string? CustomPrompt { get; init; }
      public bool EnableRag { get; init; }
      public bool EnableWebSearch { get; init; }
      public bool EnableExtendedThinking { get; init; }
    }
  }
}
