using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI_API.Chat;

namespace duetGPT.Services;

public class DeepSeekCompletionResponse
{
  public string Content { get; set; }
  public int InputTokens { get; set; }
  public int OutputTokens { get; set; }
}

public class DeepSeekService
{
  private readonly OpenAI_API.OpenAIAPI _client;
  private readonly ILogger<DeepSeekService> _logger;

  public DeepSeekService(IConfiguration config, ILogger<DeepSeekService> logger)
  {
    _logger = logger;
    var apiKey = config["DeepSeek:ApiKey"]
        ?? throw new ArgumentNullException("DeepSeek:ApiKey configuration missing");

    _client = new OpenAI_API.OpenAIAPI(new OpenAI_API.APIAuthentication(apiKey))
    {
      ApiUrlFormat = "https://api.deepseek.com/v1/{0}/{1}"
    };
  }

  public async Task<DeepSeekCompletionResponse> GetCompletionAsync(string userMessage, string systemPrompt, string model = "deepseek-chat")
  {
    try
    {
      var messages = new List<ChatMessage>
            {
                new ChatMessage { Role = ChatMessageRole.System, Content = systemPrompt },
                new ChatMessage { Role = ChatMessageRole.User, Content = userMessage }
            };

      var request = new ChatRequest()
      {
        Model = model,
        Messages = messages,
        Temperature = 0.7
      };

      var response = await _client.Chat.CreateChatCompletionAsync(request);

      return new DeepSeekCompletionResponse
      {
        Content = response.Choices[0].Message.Content,
        InputTokens = response.Usage.PromptTokens,
        OutputTokens = response.Usage.CompletionTokens
      };
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "DeepSeek API error");
      throw new ApplicationException("DeepSeek service unavailable", ex);
    }
  }

  public async Task<string> GetChatCompletionAsync(IList<ChatMessage> messages, string model = "deepseek-chat")
  {
    try
    {
      var request = new ChatRequest()
      {
        Model = model,
        Messages = messages,
        Temperature = 0.7
      };

      var response = await _client.Chat.CreateChatCompletionAsync(request);
      return response.Choices[0].Message.Content;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "DeepSeek API error");
      throw new ApplicationException("DeepSeek service unavailable", ex);
    }
  }
}