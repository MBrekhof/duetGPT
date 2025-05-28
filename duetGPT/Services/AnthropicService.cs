using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using duetGPT.Data;
using Microsoft.Extensions.Logging;

namespace duetGPT.Services
{
    public class AnthropicService
    {
        private readonly AnthropicClient _anthropicClient;
        private readonly ILogger<AnthropicService> _logger;
        private readonly IConfiguration _configuration;

        public AnthropicService(IConfiguration configuration, ILogger<AnthropicService> logger)
        {
            _logger = logger;
            _configuration = configuration;
            try
            {
                var apiKey = configuration["Anthropic:ApiKey"];
                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogError("Anthropic API key is not configured");
                    throw new ArgumentException("Anthropic API key is not configured.");
                }
                _anthropicClient = new AnthropicClient(apiKey);
                _logger.LogInformation("AnthropicService initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing AnthropicService");
                throw;
            }
        }

        public AnthropicClient GetAnthropicClient()
        {
            try
            {
                _logger.LogDebug("Retrieving Anthropic client");
                return _anthropicClient;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Anthropic client");
                throw;
            }
        }

        /// <summary>
        /// Sends a message to Claude with extended thinking enabled using the AnthropicClient
        /// </summary>
        /// <param name="request">The extended message request</param>
        /// <returns>Response with thinking content</returns>
        public async Task<ExtendedMessageResponse> SendMessageWithExtendedThinkingAsync(ExtendedMessageRequest request)
        {
            try
            {
                _logger.LogInformation("Sending message with extended thinking enabled using AnthropicClient");

                // Convert custom request to SDK MessageParameters
                var messages = new List<Message>();
                foreach (var msg in request.Messages)
                {
                    messages.Add(new Message(
                        msg.Role == "user" ? RoleType.User : RoleType.Assistant,
                        msg.Content
                    ));
                }

                var parameters = new MessageParameters()
                {
                    Model = request.Model,
                    Messages = messages,
                    System = !string.IsNullOrEmpty(request.System) ? new List<SystemMessage> { new SystemMessage(request.System) } : null,
                    MaxTokens = request.MaxTokens,
                    Temperature = request.Temperature,
                    Stream = false,
                    Thinking = request.Thinking != null ? new Anthropic.SDK.Messaging.ThinkingParameters()
                    {
                        BudgetTokens = request.Thinking.BudgetTokens
                    } : null
                };

                _logger.LogDebug("Calling AnthropicClient with parameters: Model={Model}, MaxTokens={MaxTokens}, Temperature={Temperature}, ThinkingBudget={ThinkingBudget}",
                    parameters.Model, parameters.MaxTokens, parameters.Temperature, parameters.Thinking?.BudgetTokens);

                // Use the AnthropicClient to send the message
                var response = await _anthropicClient.Messages.GetClaudeMessageAsync(parameters);

                if (response == null)
                {
                    throw new InvalidOperationException("Failed to get response from Anthropic API");
                }

                // Convert SDK response to custom ExtendedMessageResponse
                var result = new ExtendedMessageResponse
                {
                    Id = response.Id,
                    Type = response.Type,
                    Role = response.Role.ToString().ToLower(),
                    Model = response.Model,
                    StopReason = response.StopReason,
                    StopSequence = response.StopSequence?.ToString(),
                    Content = new List<ContentItem>(),
                    Usage = new UsageInfo
                    {
                        InputTokens = response.Usage.InputTokens,
                        OutputTokens = response.Usage.OutputTokens
                    }
                };

                // Convert content items
                if (response.Content != null)
                {
                    foreach (var content in response.Content)
                    {
                        if (content is TextContent textContent)
                        {
                            result.Content.Add(new ContentItem
                            {
                                Type = "text",
                                Text = textContent.Text
                            });
                        }
                        else if (content is ThinkingContent thinking)
                        {
                            result.Content.Add(new ContentItem
                            {
                                Type = "thinking",
                                Text = thinking.ToString()
                            });
                        }
                    }
                }

                // Extract thinking content for logging
                var extractedThinkingContent = result.GetThinkingContent();
                if (string.IsNullOrEmpty(extractedThinkingContent))
                {
                    _logger.LogWarning("No thinking content found in the response.");
                }
                else
                {
                    _logger.LogInformation("Thinking content found with length: {Length}", extractedThinkingContent.Length);
                }

                _logger.LogInformation("Successfully received extended thinking response using AnthropicClient");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message with extended thinking using AnthropicClient: {Message}", ex.Message);
                throw;
            }
        }
    }
}
