using duetGPT.Data;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace duetGPT.Services
{
  public interface IKnowledgeService
  {
    Task<List<KnowledgeResult>> GetRelevantKnowledgeAsync(string userQuestion);
    Task<Knowledge> SaveKnowledgeAsync(string content, string title, string metadata, string userId);
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
      // Maximum allowed absolute distance from 1
      const double MaxDistanceThreshold = 0.25; // Adjust this value as needed

      try
      {
        // Get embedding for user question
        var embeddingResult = await _openAIService.GetVectorDataAsync(userQuestion);
        _logger.LogInformation("Generated embedding for question. Tokens: {TokenCount}, Cost: ${Cost}",
            embeddingResult.TokenCount, embeddingResult.Cost.ToString("F6"));

        // Build the SQL query with direct vector syntax and include metadata
        var sql = "SELECT ragdataid as Id, ragcontent, metadata, vectordatastring, " +
                 "(vectordatastring <-> '" + embeddingResult.Vector + "'::vector) as distance " +
                 "FROM ragdata " +
                 "WHERE vectordatastring IS NOT NULL " +
                 "ORDER BY distance " +
                 "LIMIT 3";

        var queryResults = await _dbContext.Set<KnowledgeQueryResult>()
            .FromSqlRaw(sql)
            .ToListAsync();

        // Update embedding costs for the retrieved knowledge items
        if (queryResults.Any())
        {
          try
          {
            // Calculate cost per item (distribute evenly among retrieved items)
            decimal costPerItem = embeddingResult.Cost / queryResults.Count;

            foreach (var result in queryResults)
            {
              // Find the corresponding Knowledge entity and update its embedding cost
              var knowledge = await _dbContext.Set<Knowledge>()
                  .Where(k => k.RagDataId == result.Id)
                  .FirstOrDefaultAsync();

              if (knowledge != null)
              {
                // Add the new embedding cost to the existing one
                knowledge.EmbeddingCost = (knowledge.EmbeddingCost ?? 0) + costPerItem;
                _logger.LogInformation("Updated embedding cost for knowledge ID {Id} to {Cost}",
                    knowledge.RagDataId, knowledge.EmbeddingCost);
              }
            }

            // Save the changes to the database
            await _dbContext.SaveChangesAsync();
          }
          catch (Exception ex)
          {
            _logger.LogError(ex, "Error updating embedding costs");
            // Don't throw - we still want to return the knowledge results
          }
        }

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

        // Filter out results where absolute distance from 1 exceeds threshold
        relevantKnowledge = relevantKnowledge
            .Where(k => Math.Abs(1 - k.Distance) <= MaxDistanceThreshold)
            .ToList();

        _logger.LogInformation($"Question: {userQuestion}");
        foreach (var result in queryResults)
        {
          _logger.LogInformation($"Distance: {result.distance:F3}, Content: {result.Ragcontent.Substring(0, Math.Min(50, result.Ragcontent.Length))}...");
        }

        return relevantKnowledge ?? new List<KnowledgeResult>();
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error getting relevant knowledge");
        return new List<KnowledgeResult>();
      }
    }

    public async Task<Knowledge> SaveKnowledgeAsync(string content, string title, string metadata, string userId)
    {
      try
      {
        // Get embedding for the content
        var embeddingResult = await _openAIService.GetVectorDataAsync(content);
        _logger.LogInformation("Generated embedding for content. Tokens: {TokenCount}, Cost: ${Cost}",
            embeddingResult.TokenCount, embeddingResult.Cost.ToString("F6"));

        var knowledge = new Knowledge
        {
          RagContent = content,
          Title = title,
          Metadata = metadata,
          OwnerId = userId,
          VectorDataString = embeddingResult.Vector,
          CreationDate = DateTime.UtcNow,
          Tokens = embeddingResult.TokenCount,
          EmbeddingCost = embeddingResult.Cost
        };

        _dbContext.Add(knowledge);
        await _dbContext.SaveChangesAsync();

        return knowledge;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error saving knowledge");
        throw;
      }
    }
  }
}
