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
  public class ThreadsController : ControllerBase
  {
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly IThreadService _threadService;
    private readonly ILogger<ThreadsController> _logger;

    public ThreadsController(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IThreadService threadService,
        ILogger<ThreadsController> logger)
    {
      _dbContextFactory = dbContextFactory;
      _threadService = threadService;
      _logger = logger;
    }

    private string GetUserId()
    {
      return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
          ?? throw new UnauthorizedAccessException("User ID not found");
    }

    // GET: api/threads
    [HttpGet]
    public async Task<ActionResult<List<ThreadDto>>> GetThreads()
    {
      try
      {
        var userId = GetUserId();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var threads = await dbContext.Threads
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.StartTime)
            .Select(t => new ThreadDto
            {
              Id = t.Id,
              UserId = t.UserId,
              Title = t.Title,
              TotalTokens = t.TotalTokens,
              TotalCost = t.Cost,
              CreatedAt = t.StartTime,
              UpdatedAt = t.StartTime
            })
            .ToListAsync();

        return Ok(threads);
      }
      catch (UnauthorizedAccessException ex)
      {
        return Unauthorized(new { message = ex.Message });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error getting threads");
        return StatusCode(500, new { message = "An error occurred while retrieving threads" });
      }
    }

    // GET: api/threads/{id}
    [HttpGet("{id}")]
    public async Task<ActionResult<ThreadDto>> GetThread(int id)
    {
      try
      {
        var userId = GetUserId();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var thread = await dbContext.Threads
            .Where(t => t.Id == id && t.UserId == userId)
            .Select(t => new ThreadDto
            {
              Id = t.Id,
              UserId = t.UserId,
              Title = t.Title,
              TotalTokens = t.TotalTokens,
              TotalCost = t.Cost,
              CreatedAt = t.StartTime,
              UpdatedAt = t.StartTime
            })
            .FirstOrDefaultAsync();

        if (thread == null)
          return NotFound(new { message = "Thread not found" });

        return Ok(thread);
      }
      catch (UnauthorizedAccessException ex)
      {
        return Unauthorized(new { message = ex.Message });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error getting thread {ThreadId}", id);
        return StatusCode(500, new { message = "An error occurred while retrieving the thread" });
      }
    }

    // POST: api/threads
    [HttpPost]
    public async Task<ActionResult<ThreadDto>> CreateThread([FromBody] CreateThreadRequest request)
    {
      try
      {
        var userId = GetUserId();
        var thread = await _threadService.CreateThreadAsync(userId);

        // Update title if provided
        if (!string.IsNullOrEmpty(request.Title))
        {
          await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
          var dbThread = await dbContext.Threads.FindAsync(thread.Id);
          if (dbThread != null)
          {
            dbThread.Title = request.Title;
            await dbContext.SaveChangesAsync();
            thread.Title = request.Title;
          }
        }

        var dto = new ThreadDto
        {
          Id = thread.Id,
          UserId = thread.UserId,
          Title = thread.Title,
          TotalTokens = thread.TotalTokens,
          TotalCost = thread.Cost,
          CreatedAt = thread.StartTime,
          UpdatedAt = thread.StartTime
        };

        return CreatedAtAction(nameof(GetThread), new { id = thread.Id }, dto);
      }
      catch (UnauthorizedAccessException ex)
      {
        return Unauthorized(new { message = ex.Message });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error creating thread");
        return StatusCode(500, new { message = "An error occurred while creating the thread" });
      }
    }

    // DELETE: api/threads/{id}
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteThread(int id)
    {
      try
      {
        var userId = GetUserId();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var thread = await dbContext.Threads
            .Where(t => t.Id == id && t.UserId == userId)
            .FirstOrDefaultAsync();

        if (thread == null)
          return NotFound(new { message = "Thread not found" });

        dbContext.Threads.Remove(thread);
        await dbContext.SaveChangesAsync();

        return NoContent();
      }
      catch (UnauthorizedAccessException ex)
      {
        return Unauthorized(new { message = ex.Message });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error deleting thread {ThreadId}", id);
        return StatusCode(500, new { message = "An error occurred while deleting the thread" });
      }
    }

    // GET: api/threads/{id}/messages
    [HttpGet("{id}/messages")]
    public async Task<ActionResult<List<MessageDto>>> GetMessages(int id)
    {
      try
      {
        var userId = GetUserId();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        // Verify thread belongs to user
        var threadExists = await dbContext.Threads
            .AnyAsync(t => t.Id == id && t.UserId == userId);

        if (!threadExists)
          return NotFound(new { message = "Thread not found" });

        var messages = await dbContext.Messages
            .Where(m => m.ThreadId == id)
            .OrderBy(m => m.Created)
            .Select(m => new MessageDto
            {
              Id = m.Id,
              ThreadId = m.ThreadId,
              Role = m.Role,
              Content = m.Content,
              Tokens = m.TokenCount,
              CreatedAt = m.Created
            })
            .ToListAsync();

        return Ok(messages);
      }
      catch (UnauthorizedAccessException ex)
      {
        return Unauthorized(new { message = ex.Message });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error getting messages for thread {ThreadId}", id);
        return StatusCode(500, new { message = "An error occurred while retrieving messages" });
      }
    }
  }

  // DTOs
  public class ThreadDto
  {
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int TotalTokens { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
  }

  public class MessageDto
  {
    public int Id { get; set; }
    public int ThreadId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int? Tokens { get; set; }
    public DateTime CreatedAt { get; set; }
  }

  public class CreateThreadRequest
  {
    public string Title { get; set; } = "New Chat";
  }
}
