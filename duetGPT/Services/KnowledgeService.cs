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

        // Build the SQL query with direct vector syntax
        var sql = "SELECT ragdataid as Id, ragcontent, vectordatastring, " +
                 "(vectordatastring <-> '" + questionEmbedding + "'::vector) as distance " +
                 "FROM ragdata " +
                 "WHERE vectordatastring IS NOT NULL " +
                 "ORDER BY distance " +
                 "LIMIT 3";

        var queryResults = await _dbContext.Set<KnowledgeQueryResult>()
            .FromSqlRaw(sql)
            .ToListAsync();

        // Map to KnowledgeResult
        var relevantKnowledge = queryResults.Select(k => new KnowledgeResult
        {
          Content = k.Ragcontent,
          Distance = k.distance
        }).ToList();

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
