using DevExpress.Pdf.Native.BouncyCastle.Asn1.X509;
using DevExpress.RichEdit.Export;
using OpenAI;
using Pgvector;
using Microsoft.Extensions.Logging;

namespace duetGPT.Services
{
    public class OpenAIService
    {
        private readonly OpenAIClient _openAIClient;
        private string? _embedding;
        private readonly ILogger<OpenAIService> _logger;

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

        public async Task<Vector> GetVectorDataAsync(string content)
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

                _logger.LogInformation("Successfully generated vector data");
                return vector;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting vector data");
                throw;
            }
        }
    }
}
