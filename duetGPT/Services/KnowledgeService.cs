using duetGPT.Data;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace duetGPT.Services
{
  public interface IKnowledgeService
  {
    Task<List<KnowledgeResult>> GetRelevantKnowledgeAsync(string userQuestion);
  }

  public class KnowledgeService : IKnowledgeService
  {
    private readonly ApplicationDbContext _dbContext;
    private readonly OpenAIService _openAIService;
    private readonly ILogger<KnowledgeService> _logger;

    public KnowledgeService(
        ApplicationDbContext dbContext,
        OpenAIService openAIService,
        ILogger<KnowledgeService> logger)
    {
      _dbContext = dbContext;
      _openAIService = openAIService;
      _logger = logger;
    }

    public async Task<List<KnowledgeResult>> GetRelevantKnowledgeAsync(string userQuestion)
    {
      try
      {
        // Get embedding for user question
        var questionEmbedding = await _openAIService.GetVectorDataAsync(userQuestion);

        // Build the SQL query with direct vector syntax and include metadata
        var sql = "SELECT ragdataid as Id, ragcontent, metadata, vectordatastring, " +
                 "(vectordatastring <-> '" + questionEmbedding + "'::vector) as distance " +
                 "FROM ragdata " +
                 "WHERE vectordatastring IS NOT NULL " +
                 "ORDER BY distance " +
                 "LIMIT 3";

        var queryResults = await _dbContext.Set<KnowledgeQueryResult>()
            .FromSqlRaw(sql)
            .ToListAsync();

        // Map to KnowledgeResult with metadata
        var relevantKnowledge = queryResults.Select(k => new KnowledgeResult
        {
          Content = k.Ragcontent,
          Distance = k.distance,
          Metadata = k.Metadata
        }).ToList();

        // Boost relevance scores based on metadata
        foreach (var knowledge in relevantKnowledge)
        {
          if (!string.IsNullOrEmpty(knowledge.Metadata))
          {
            // Headers and high importance content get a relevance boost
            if (knowledge.Metadata.Contains("[type: header]") ||
                knowledge.Metadata.Contains("[importance: high]"))
            {
              knowledge.Distance *= 0.8f; // Reduce distance = increase relevance
            }

            // Boost content with matching key phrases
            if (knowledge.Metadata.Contains("key_phrases") &&
                userQuestion.Split(' ').Any(word =>
                    knowledge.Metadata.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
              knowledge.Distance *= 0.9f;
            }
          }
        }

        // Re-sort after applying boosts
        relevantKnowledge = relevantKnowledge.OrderBy(k => k.Distance).ToList();

        return relevantKnowledge ?? new List<KnowledgeResult>();
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error getting relevant knowledge");
        return new List<KnowledgeResult>();
      }
    }
  }
}
