using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using duetGPT.Data;
using duetGPT.Services;
using System.Security.Claims;

namespace duetGPT.Controllers
{
  [ApiController]
  [Route("api/[controller]")]
  [Authorize]
  public class KnowledgeController : ControllerBase
  {
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly IKnowledgeService _knowledgeService;
    private readonly ILogger<KnowledgeController> _logger;

    public KnowledgeController(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IKnowledgeService knowledgeService,
        ILogger<KnowledgeController> logger)
    {
      _dbContextFactory = dbContextFactory;
      _knowledgeService = knowledgeService;
      _logger = logger;
    }

    private string GetUserId()
    {
      return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
          ?? throw new UnauthorizedAccessException("User ID not found");
    }

    // GET: api/knowledge
    [HttpGet]
    public async Task<ActionResult<List<KnowledgeDto>>> GetKnowledge()
    {
      try
      {
        var userId = GetUserId();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var knowledgeItems = await dbContext.Set<Knowledge>()
            .Where(k => k.OwnerId == userId)
            .OrderByDescending(k => k.CreationDate)
            .Select(k => new KnowledgeDto
            {
              Id = k.RagDataId,
              Content = k.RagContent ?? string.Empty,
              Metadata = k.Metadata,
              CreatedAt = k.CreationDate ?? DateTime.UtcNow
            })
            .ToListAsync();

        return Ok(knowledgeItems);
      }
      catch (UnauthorizedAccessException ex)
      {
        return Unauthorized(new { message = ex.Message });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error getting knowledge");
        return StatusCode(500, new { message = "An error occurred while retrieving knowledge" });
      }
    }

    // POST: api/knowledge
    [HttpPost]
    public async Task<ActionResult<KnowledgeDto>> SaveKnowledge([FromBody] SaveKnowledgeRequest request)
    {
      try
      {
        var userId = GetUserId();
        var knowledge = await _knowledgeService.SaveKnowledgeAsync(
            request.Content,
            string.Empty, // title
            request.Metadata ?? string.Empty,
            userId
        );

        var dto = new KnowledgeDto
        {
          Id = knowledge.RagDataId,
          Content = knowledge.RagContent ?? string.Empty,
          Metadata = knowledge.Metadata,
          CreatedAt = knowledge.CreationDate ?? DateTime.UtcNow
        };

        return CreatedAtAction(nameof(GetKnowledge), new { id = knowledge.RagDataId }, dto);
      }
      catch (UnauthorizedAccessException ex)
      {
        return Unauthorized(new { message = ex.Message });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error saving knowledge");
        return StatusCode(500, new { message = "An error occurred while saving knowledge" });
      }
    }
  }

  // DTOs
  public class KnowledgeDto
  {
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
  }

  public class SaveKnowledgeRequest
  {
    public string Content { get; set; } = string.Empty;
    public string? Metadata { get; set; }
  }
}
