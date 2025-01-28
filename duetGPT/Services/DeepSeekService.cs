using DeepSeek.Core;
using DeepSeek.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace duetGPT.Services
{
  /// <summary>
  /// Response model for DeepSeek completions containing content and token usage
  /// </summary>
  public class DeepSeekCompletionResponse
  {
    /// <summary>
    /// The content of the completion response
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Number of tokens in the input
    /// </summary>
    public int InputTokens { get; set; }

    /// <summary>
    /// Number of tokens in the output
    /// </summary>
    public int OutputTokens { get; set; }
  }

  /// <summary>
  /// Service for interacting with the DeepSeek API using the official SDK
  /// </summary>
  public class DeepSeekService
  {
    private readonly DeepSeekClient _client;
    private readonly ILogger<DeepSeekService> _logger;
    private const string DEFAULT_MODEL = DeepSeekModels.ChatModel;
    private const string API_BASE_URL = "https://api.deepseek.com/v1";
    private const int TIMEOUT_SECONDS = 60;

    /// <summary>
    /// Initializes a new instance of the DeepSeekService
    /// </summary>
    /// <param name="config">Configuration to get API key</param>
    /// <param name="logger">Logger for error handling</param>
    /// <exception cref="ArgumentNullException">Thrown when DeepSeek:ApiKey configuration is missing</exception>
    public DeepSeekService(IConfiguration config, ILogger<DeepSeekService> logger)
    {
      _logger = logger;

      var apiKey = config["DeepSeek:ApiKey"];
      if (string.IsNullOrEmpty(apiKey))
      {
        const string message = "DeepSeek:ApiKey configuration is missing";
        _logger.LogError(message);
        throw new ArgumentNullException(nameof(apiKey), message);
      }

      if (!apiKey.StartsWith("sk-"))
      {
        const string message = "Invalid DeepSeek API key format. Key should start with 'sk-'";
        _logger.LogError(message);
        throw new ArgumentException(message, nameof(apiKey));
      }

      try
      {
        _client = new DeepSeekClient(apiKey);
        _logger.LogInformation("DeepSeek service initialized successfully");
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to initialize DeepSeek client");
        throw new ApplicationException("Failed to initialize DeepSeek service", ex);
      }
    }

    /// <summary>
    /// Gets a completion response from DeepSeek API
    /// </summary>
    /// <param name="userMessage">The user's input message</param>
    /// <param name="systemPrompt">The system prompt to guide the model's behavior</param>
    /// <param name="model">The model to use, defaults to chat model (DeepSeek-V3)</param>
    /// <returns>A DeepSeekCompletionResponse containing the response and token usage</returns>
    /// <exception cref="ApplicationException">Thrown when the DeepSeek service is unavailable</exception>
    public async Task<DeepSeekCompletionResponse> GetCompletionAsync(
        string userMessage,
        string systemPrompt,
        string model = DEFAULT_MODEL)
    {
      if (string.IsNullOrEmpty(userMessage))
        throw new ArgumentException("User message cannot be empty", nameof(userMessage));

      if (string.IsNullOrEmpty(model))
        throw new ArgumentException("Model cannot be empty", nameof(model));

      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECONDS));

      try
      {
        _logger.LogInformation(
            "Sending request to DeepSeek API. Model: {Model}, SystemPrompt Length: {SystemPromptLength}, UserMessage Length: {UserMessageLength}",
            model, systemPrompt?.Length ?? 0, userMessage.Length);

        var request = new ChatRequest
        {
          Messages = new List<Message>
                    {
                        Message.NewSystemMessage(systemPrompt ?? string.Empty),
                        Message.NewUserMessage(userMessage)
                    },
          Model = model,
          Temperature = 0.7f,
          MaxTokens = 4096
        };

        _logger.LogDebug("Request payload: {Request}", JsonSerializer.Serialize(request));

        var response = await _client.ChatAsync(request, cts.Token);

        if (response == null)
        {
          var errorMessage = $"DeepSeek API returned null response. Error: {_client.ErrorMsg}";
          _logger.LogError(errorMessage);
          throw new ApplicationException(errorMessage);
        }

        _logger.LogDebug("Raw API response: {Response}", JsonSerializer.Serialize(response));

        if (response.Choices == null || response.Choices.Count == 0)
        {
          var errorMessage = "DeepSeek API returned no choices in response";
          _logger.LogError(errorMessage);
          throw new ApplicationException(errorMessage);
        }

        var result = new DeepSeekCompletionResponse
        {
          Content = response.Choices[0].Message?.Content ?? string.Empty,
          InputTokens = response.Usage?.PromptTokens ?? 0,
          OutputTokens = response.Usage?.CompletionTokens ?? 0
        };

        if (string.IsNullOrEmpty(result.Content))
        {
          _logger.LogWarning("DeepSeek API returned empty content");
        }

        _logger.LogInformation(
            "Successfully received response from DeepSeek API. Content Length: {ContentLength}, Input Tokens: {InputTokens}, Output Tokens: {OutputTokens}",
            result.Content.Length, result.InputTokens, result.OutputTokens);

        return result;
      }
      catch (JsonException ex)
      {
        var errorMessage = $"Error parsing DeepSeek API response: {ex.Message}";
        _logger.LogError(ex, errorMessage);
        throw new ApplicationException(errorMessage, ex);
      }
      catch (HttpRequestException ex)
      {
        var errorMessage = $"Network error calling DeepSeek API: {ex.Message}";
        _logger.LogError(ex, errorMessage);
        throw new ApplicationException(errorMessage, ex);
      }
      catch (OperationCanceledException)
      {
        var errorMessage = $"DeepSeek API request timed out after {TIMEOUT_SECONDS} seconds";
        _logger.LogError(errorMessage);
        throw new ApplicationException(errorMessage);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error calling DeepSeek API: {ErrorMessage}", ex.Message);
        throw new ApplicationException($"DeepSeek service unavailable: {ex.Message}", ex);
      }
    }

    /// <summary>
    /// Gets a chat completion from DeepSeek API using a list of messages
    /// </summary>
    /// <param name="messages">The list of chat messages</param>
    /// <param name="model">The model to use, defaults to chat model (DeepSeek-V3)</param>
    /// <returns>The response content from the model</returns>
    /// <exception cref="ApplicationException">Thrown when the DeepSeek service is unavailable</exception>
    public async Task<string> GetChatCompletionAsync(
        IList<Message> messages,
        string model = DEFAULT_MODEL)
    {
      if (messages == null || messages.Count == 0)
        throw new ArgumentException("Messages list cannot be null or empty", nameof(messages));

      if (string.IsNullOrEmpty(model))
        throw new ArgumentException("Model cannot be empty", nameof(model));

      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECONDS));

      try
      {
        _logger.LogInformation(
            "Sending chat request to DeepSeek API. Model: {Model}, Message Count: {MessageCount}",
            model, messages.Count);

        var request = new ChatRequest
        {
          Messages = messages.ToList(),
          Model = model,
          Temperature = 0.7f,
          MaxTokens = 4096
        };

        _logger.LogDebug("Request payload: {Request}", JsonSerializer.Serialize(request));

        var response = await _client.ChatAsync(request, cts.Token);

        if (response == null)
        {
          var errorMessage = $"DeepSeek API returned null response. Error: {_client.ErrorMsg}";
          _logger.LogError(errorMessage);
          throw new ApplicationException(errorMessage);
        }

        _logger.LogDebug("Raw API response: {Response}", JsonSerializer.Serialize(response));

        if (response.Choices == null || response.Choices.Count == 0)
        {
          var errorMessage = "DeepSeek API returned no choices in response";
          _logger.LogError(errorMessage);
          throw new ApplicationException(errorMessage);
        }

        var content = response.Choices[0].Message?.Content ?? string.Empty;

        if (string.IsNullOrEmpty(content))
        {
          _logger.LogWarning("DeepSeek API returned empty content");
        }

        _logger.LogInformation(
            "Successfully received chat response from DeepSeek API. Content Length: {ContentLength}",
            content.Length);

        return content;
      }
      catch (JsonException ex)
      {
        var errorMessage = $"Error parsing DeepSeek API response: {ex.Message}";
        _logger.LogError(ex, errorMessage);
        throw new ApplicationException(errorMessage, ex);
      }
      catch (HttpRequestException ex)
      {
        var errorMessage = $"Network error calling DeepSeek API: {ex.Message}";
        _logger.LogError(ex, errorMessage);
        throw new ApplicationException(errorMessage, ex);
      }
      catch (OperationCanceledException)
      {
        var errorMessage = $"DeepSeek API request timed out after {TIMEOUT_SECONDS} seconds";
        _logger.LogError(errorMessage);
        throw new ApplicationException(errorMessage);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error calling DeepSeek API: {ErrorMessage}", ex.Message);
        throw new ApplicationException($"DeepSeek service unavailable: {ex.Message}", ex);
      }
    }

    /// <summary>
    /// Gets a streaming chat completion from DeepSeek API
    /// </summary>
    /// <param name="messages">The list of chat messages</param>
    /// <param name="model">The model to use, defaults to chat model (DeepSeek-V3)</param>
    /// <returns>An async enumerable of choices containing the streamed response</returns>
    /// <exception cref="ApplicationException">Thrown when the DeepSeek service is unavailable</exception>
    public async Task<IAsyncEnumerable<Choice>?> GetStreamingChatCompletionAsync(
        IList<Message> messages,
        string model = DEFAULT_MODEL)
    {
      if (messages == null || messages.Count == 0)
        throw new ArgumentException("Messages list cannot be null or empty", nameof(messages));

      if (string.IsNullOrEmpty(model))
        throw new ArgumentException("Model cannot be empty", nameof(model));

      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECONDS));

      try
      {
        _logger.LogInformation(
            "Starting streaming chat request to DeepSeek API. Model: {Model}, Message Count: {MessageCount}",
            model, messages.Count);

        var request = new ChatRequest
        {
          Messages = messages.ToList(),
          Model = model,
          Temperature = 0.7f,
          MaxTokens = 4096,
          Stream = true
        };

        _logger.LogDebug("Request payload: {Request}", JsonSerializer.Serialize(request));

        var stream = await _client.ChatStreamAsync(request, cts.Token);

        if (stream == null)
        {
          var errorMessage = $"DeepSeek API returned null stream. Error: {_client.ErrorMsg}";
          _logger.LogError(errorMessage);
          throw new ApplicationException(errorMessage);
        }

        _logger.LogInformation("Successfully started streaming response from DeepSeek API");
        return stream;
      }
      catch (JsonException ex)
      {
        var errorMessage = $"Error parsing DeepSeek API response: {ex.Message}";
        _logger.LogError(ex, errorMessage);
        throw new ApplicationException(errorMessage, ex);
      }
      catch (HttpRequestException ex)
      {
        var errorMessage = $"Network error calling DeepSeek API: {ex.Message}";
        _logger.LogError(ex, errorMessage);
        throw new ApplicationException(errorMessage, ex);
      }
      catch (OperationCanceledException)
      {
        var errorMessage = $"DeepSeek API streaming request timed out after {TIMEOUT_SECONDS} seconds";
        _logger.LogError(errorMessage);
        throw new ApplicationException(errorMessage);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error starting streaming from DeepSeek API: {ErrorMessage}", ex.Message);
        throw new ApplicationException($"DeepSeek service unavailable: {ex.Message}", ex);
      }
    }
  }
}