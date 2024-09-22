using Anthropic.SDK;
using Microsoft.Extensions.Configuration;

namespace duetGPT.Services
{
    public class AnthropicService
    {
        private readonly AnthropicClient _anthropicClient;

        public AnthropicService(IConfiguration configuration)
        {
            var apiKey = configuration["Anthropic:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new ArgumentException("Anthropic API key is not configured.");
            }
            _anthropicClient = new AnthropicClient(apiKey);
        }

        public AnthropicClient GetAnthropicClient()
        {
            return _anthropicClient;
        }
    }
}
