

using DevExpress.Pdf.Native.BouncyCastle.Asn1.X509;
using DevExpress.RichEdit.Export;
using OpenAI;
using Pgvector;

namespace duetGPT.Services
{
    public class OpenAIService
    {
        private readonly OpenAIClient _openAIClient;
        private string? _embedding;

        public OpenAIService(IConfiguration configuration)
        {
            var apiKey = configuration["OpenAI:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new ArgumentException("Anthropic API key is not configured.");
            }
            _openAIClient = new OpenAIClient(apiKey);
            _embedding = configuration["OpenAI:Embedding"];
        }

        public OpenAIClient GetOpenAIClient()
        {
            return _openAIClient;
        }

        public async Task<Vector> GetVectorDataAsync(string content)
        {
            var model = await _openAIClient.ModelsEndpoint.GetModelDetailsAsync(_embedding);

            var embeddings = await _openAIClient.EmbeddingsEndpoint.CreateEmbeddingAsync(content, model, dimensions: 1536);
            return (Vector)embeddings.Data[0].Embedding;
        }
    }
}
