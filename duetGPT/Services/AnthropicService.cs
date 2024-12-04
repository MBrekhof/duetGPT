using Anthropic.SDK;
using Microsoft.Extensions.Logging;

namespace duetGPT.Services
{
    public class AnthropicService
    {
        private readonly AnthropicClient _anthropicClient;
        private readonly ILogger<AnthropicService> _logger;

        public AnthropicService(IConfiguration configuration, ILogger<AnthropicService> logger)
        {
            _logger = logger;
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
    }
}
