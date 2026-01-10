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
  public class DocumentsController : ControllerBase
  {
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly DocumentProcessingService _documentProcessingService;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        DocumentProcessingService documentProcessingService,
        ILogger<DocumentsController> logger)
    {
      _dbContextFactory = dbContextFactory;
      _documentProcessingService = documentProcessingService;
      _logger = logger;
    }

    private string GetUserId()
    {
      return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
          ?? throw new UnauthorizedAccessException("User ID not found");
    }

    // GET: api/documents
    [HttpGet]
    public async Task<ActionResult<List<DocumentDto>>> GetDocuments()
    {
      try
      {
        var userId = GetUserId();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var documents = await dbContext.Documents
            .Where(d => d.OwnerId == userId)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new DocumentDto
            {
              Id = d.Id,
              FileName = d.FileName,
              UploadedAt = d.UploadedAt,
              Size = d.Content.Length
            })
            .ToListAsync();

        return Ok(documents);
      }
      catch (UnauthorizedAccessException ex)
      {
        return Unauthorized(new { message = ex.Message });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error getting documents");
        return StatusCode(500, new { message = "An error occurred while retrieving documents" });
      }
    }

    // POST: api/documents/upload
    [HttpPost("upload")]
    public async Task<ActionResult<DocumentDto>> UploadDocument(IFormFile file)
    {
      try
      {
        if (file == null || file.Length == 0)
          return BadRequest(new { message = "No file was uploaded" });

        var userId = GetUserId();

        // Read file content
        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream);
        var fileBytes = memoryStream.ToArray();

        // Create document
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var document = new Document
        {
          FileName = file.FileName,
          Content = fileBytes,
          ContentType = file.ContentType,
          UploadedAt = DateTime.UtcNow,
          OwnerId = userId
        };

        dbContext.Documents.Add(document);
        await dbContext.SaveChangesAsync();

        // TODO: Process document for RAG (extract text and create embeddings)
        // This would be done asynchronously in the background

        var dto = new DocumentDto
        {
          Id = document.Id,
          FileName = document.FileName,
          UploadedAt = document.UploadedAt,
          Size = fileBytes.Length
        };

        return CreatedAtAction(nameof(GetDocuments), new { id = document.Id }, dto);
      }
      catch (UnauthorizedAccessException ex)
      {
        return Unauthorized(new { message = ex.Message });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error uploading document");
        return StatusCode(500, new { message = "An error occurred while uploading the document" });
      }
    }

    // DELETE: api/documents/{id}
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteDocument(int id)
    {
      try
      {
        var userId = GetUserId();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var document = await dbContext.Documents
            .Where(d => d.Id == id && d.OwnerId == userId)
            .FirstOrDefaultAsync();

        if (document == null)
          return NotFound(new { message = "Document not found" });

        dbContext.Documents.Remove(document);
        await dbContext.SaveChangesAsync();

        return NoContent();
      }
      catch (UnauthorizedAccessException ex)
      {
        return Unauthorized(new { message = ex.Message });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error deleting document {DocumentId}", id);
        return StatusCode(500, new { message = "An error occurred while deleting the document" });
      }
    }
  }

  // DTOs
  public class DocumentDto
  {
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public int Size { get; set; }
  }
}
