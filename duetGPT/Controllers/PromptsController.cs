using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using duetGPT.Data;
using System.Security.Claims;

namespace duetGPT.Controllers
{
  [ApiController]
  [Route("api/[controller]")]
  [Authorize]
  public class PromptsController : ControllerBase
  {
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<PromptsController> _logger;

    public PromptsController(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        ILogger<PromptsController> logger)
    {
      _dbContextFactory = dbContextFactory;
      _logger = logger;
    }

    // GET: api/prompts
    [HttpGet]
    public async Task<ActionResult<List<PromptDto>>> GetPrompts()
    {
      try
      {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var prompts = await dbContext.Prompts
            .OrderBy(p => p.Name)
            .Select(p => new PromptDto
            {
              Id = p.PromptID,
              Name = p.Name ?? string.Empty,
              Content = p.Content ?? string.Empty
            })
            .ToListAsync();

        return Ok(prompts);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error getting prompts");
        return StatusCode(500, new { message = "An error occurred while retrieving prompts" });
      }
    }
  }

  // DTOs
  public class PromptDto
  {
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
  }
}
