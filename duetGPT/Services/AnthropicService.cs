using Anthropic.SDK;
using duetGPT.Data;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;

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
        /// Creates a custom HTTP client for direct API calls to Anthropic
        /// </summary>
        /// <returns>Configured HttpClient for Anthropic API</returns>
        private HttpClient GetCustomHttpClient()
        {
            var apiKey = _configuration["Anthropic:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogError("Anthropic API key is not configured");
                throw new ArgumentException("Anthropic API key is not configured.");
            }

            var client = new HttpClient();
            client.BaseAddress = new Uri("https://api.anthropic.com");
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("x-api-key", apiKey);

            // Use the latest API version
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            // Add the beta header for extended thinking
            // This is the key change - we're using the beta feature through headers
            client.DefaultRequestHeaders.Add("anthropic-beta", "thinking-2024-03-01");

            return client;
        }

        /// <summary>
        /// Sends a message to Claude with extended thinking enabled
        /// </summary>
        /// <param name="request">The extended message request</param>
        /// <returns>Response with thinking content</returns>
        public async Task<ExtendedMessageResponse> SendMessageWithExtendedThinkingAsync(ExtendedMessageRequest request)
        {
            try
            {
                _logger.LogInformation("Sending message with extended thinking enabled");
                var client = GetCustomHttpClient();

                // Create a modified request object that includes the thinking parameter
                var requestJson = new
                {
                    model = request.Model,
                    messages = request.Messages,
                    system = request.System,
                    max_tokens = request.MaxTokens,
                    temperature = request.Temperature,
                    thinking = request.Thinking  // Use the structured thinking parameter
                };

                // Serialize the request manually
                var jsonContent = JsonSerializer.Serialize(requestJson);
                _logger.LogDebug("Request JSON: {Json}", jsonContent);

                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // Log the request headers for debugging
                foreach (var header in client.DefaultRequestHeaders)
                {
                    _logger.LogDebug("Request header: {Key} = {Value}", header.Key, string.Join(", ", header.Value));
                }

                var response = await client.PostAsync("/v1/messages", content);

                // Log the response status code
                _logger.LogDebug("Response status code: {StatusCode}", response.StatusCode);

                // Read the response content even if it's an error
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Response content: {Content}", responseContent);

                // Now check if the response was successful
                response.EnsureSuccessStatusCode();

                var result = JsonSerializer.Deserialize<ExtendedMessageResponse>(responseContent);

                if (result == null)
                {
                    throw new InvalidOperationException("Failed to deserialize response from Anthropic API");
                }

                // Log whether thinking content was found
                var thinkingContent = result.GetThinkingContent();
                if (string.IsNullOrEmpty(thinkingContent))
                {
                    _logger.LogWarning("No thinking content found in the response.");
                }
                else
                {
                    _logger.LogInformation("Thinking content found with length: {Length}", thinkingContent.Length);
                }

                _logger.LogInformation("Successfully received extended thinking response");
                return result;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error sending message with extended thinking: {Message}", ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message with extended thinking: {Message}", ex.Message);
                throw;
            }
        }
    }
}
