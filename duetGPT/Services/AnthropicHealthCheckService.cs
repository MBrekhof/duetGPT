using Anthropic.SDK;

namespace duetGPT.Services
{
    public class AnthropicHealthCheckService : BackgroundService
    {
        private readonly ILogger<AnthropicHealthCheckService> _logger;
        private readonly AnthropicClient _anthropic;

        public AnthropicHealthCheckService(ILogger<AnthropicHealthCheckService> logger, AnthropicClient anthropic)
        {
            _logger = logger;
            _anthropic = anthropic;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_anthropic != null)
                {
                    _logger.LogInformation("Anthropic client is available.");
                }
                else
                {
                    _logger.LogWarning("Anthropic client is not available.");
                }
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}