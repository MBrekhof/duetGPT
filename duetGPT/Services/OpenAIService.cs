using OpenAI;
using Pgvector;

namespace duetGPT.Services
{
    public class OpenAIService
    {
        private readonly OpenAIClient _openAIClient;
        private string? _embedding;
        private readonly ILogger<OpenAIService> _logger;
        private const decimal EMBEDDING_COST_PER_1K_TOKENS = 0.0001m;

        public OpenAIService(IConfiguration configuration, ILogger<OpenAIService> logger)
        {
            _logger = logger;
            try
            {
                var apiKey = configuration["OpenAI:ApiKey"];
                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogError("OpenAI API key is not configured");
                    throw new ArgumentException("OpenAI API key is not configured.");
                }
                _openAIClient = new OpenAIClient(apiKey);
                _embedding = configuration["OpenAI:Embedding"];
                _logger.LogInformation("OpenAIService initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing OpenAIService");
                throw;
            }
        }

        public OpenAIClient GetOpenAIClient()
        {
            return _openAIClient;
        }

        public record struct EmbeddingResult
        {
            public Vector Vector { get; init; }
            public int TokenCount { get; init; }
            public decimal Cost { get; init; }
        }

        public async Task<EmbeddingResult> GetVectorDataAsync(string content)
        {
            try
            {
                _logger.LogInformation("Getting vector data for content");
                var model = await _openAIClient.ModelsEndpoint.GetModelDetailsAsync(_embedding);
                _logger.LogDebug("Retrieved model details for embedding");

                var embeddings = await _openAIClient.EmbeddingsEndpoint.CreateEmbeddingAsync(content, model, dimensions: 1536);
                _logger.LogDebug("Created embeddings successfully");

                // Convert doubles to floats
                var floatArray = embeddings.Data[0].Embedding.Select(d => (float)d).ToArray();
                var vector = new Vector(floatArray);

                // Calculate cost based on token count
                var tokenCount = embeddings.Usage.TotalTokens ?? 0; // Default to 0 if null
                if (tokenCount == 0)
                {
                    _logger.LogWarning("Token count was null or 0 for embedding generation");
                }
                var cost = CalculateEmbeddingCost(tokenCount);

                _logger.LogInformation("Successfully generated vector data. Tokens: {TokenCount}, Cost: ${Cost}",
                    tokenCount, cost.ToString("F6"));

                return new EmbeddingResult
                {
                    Vector = vector,
                    TokenCount = tokenCount,
                    Cost = cost
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting vector data");
                throw;
            }
        }

        private static decimal CalculateEmbeddingCost(int tokens)
        {
            if (tokens < 0)
            {
                throw new ArgumentException("Token count cannot be negative", nameof(tokens));
            }

            try
            {
                return tokens * EMBEDDING_COST_PER_1K_TOKENS / 1000m;
            }
            catch (OverflowException ex)
            {
                throw new OverflowException("Embedding cost calculation resulted in overflow. Please check token count.", ex);
            }
        }
    }
}
