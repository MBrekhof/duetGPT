using Anthropic.SDK.Messaging;
using duetGPT.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using DevExpress.Pdf;
using DevExpress.XtraRichEdit;
using System.Text;

namespace duetGPT.Services
{
  public interface IThreadService
  {
    Task<DuetThread> CreateThreadAsync(string userId, string? selectedPrompt = null);
    Task<List<Message>> LoadThreadMessagesAsync(int threadId);
    Task AssociateDocumentsWithThreadAsync(DuetThread thread, IEnumerable<int> docIds);
    Task<List<string>> GetThreadDocumentContentsAsync(DuetThread thread);
    Task UpdateThreadMetricsAsync(DuetThread thread, int tokens, decimal cost);
  }

  public class ThreadService : IThreadService
  {
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<ThreadService> _logger;

    public ThreadService(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        ILogger<ThreadService> logger)
    {
      _dbContextFactory = dbContextFactory;
      _logger = logger;
    }

    public async Task<DuetThread> CreateThreadAsync(string userId, string? selectedPrompt = null)
    {
      await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
      try
      {
        if (string.IsNullOrEmpty(userId))
        {
          _logger.LogWarning("Attempted to create a new thread without a valid user ID");
          throw new InvalidOperationException("User ID is required to create a new thread");
        }

        _logger.LogInformation("Creating new thread for user {UserId}", userId);
        var thread = new DuetThread
        {
          UserId = userId,
          StartTime = DateTime.UtcNow,
          TotalTokens = 0,
          Cost = 0,
          Title = "Not yet created" // Set initial title
        };

        // Save the thread immediately
        dbContext.Threads.Add(thread);
        await dbContext.SaveChangesAsync();

        _logger.LogInformation("New thread created with ID {ThreadId}", thread.Id);
        return thread;
      }
      catch (DbUpdateException ex)
      {
        _logger.LogError(ex, "Database error while creating new thread");
        throw;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error creating new thread");
        throw;
      }
    }

    public async Task<List<Message>> LoadThreadMessagesAsync(int threadId)
    {
      await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
      try
      {
        _logger.LogInformation("Loading messages for thread {ThreadId}", threadId);

        var messages = await dbContext.Messages
            .Where(m => m.ThreadId == threadId)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();

        var anthropicMessages = new List<Message>();
        foreach (var msg in messages)
        {
          var role = msg.Role == "user" ? RoleType.User : RoleType.Assistant;
          anthropicMessages.Add(new Message(role, msg.Content));
        }

        _logger.LogInformation("Loaded {Count} messages for thread {ThreadId}", anthropicMessages.Count, threadId);
        return anthropicMessages;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error loading messages for thread {ThreadId}", threadId);
        throw;
      }
    }

    public async Task AssociateDocumentsWithThreadAsync(DuetThread thread, IEnumerable<int> docIds)
    {
      if (thread == null || !docIds.Any()) return;

      await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
      try
      {
        _logger.LogInformation("Associating documents with thread {ThreadId}", thread.Id);
        var selectedDocuments = await dbContext.Documents
            .Where(d => docIds.Contains(d.Id))
            .ToListAsync();

        var dbThread = await dbContext.Threads
            .Include(t => t.ThreadDocuments)
            .FirstOrDefaultAsync(t => t.Id == thread.Id);

        if (dbThread == null)
        {
          _logger.LogError("Thread not found in database");
          return;
        }

        if (dbThread.ThreadDocuments == null)
        {
          dbThread.ThreadDocuments = new List<ThreadDocument>();
        }

        var newThreadDocuments = selectedDocuments
            .Select(document => new ThreadDocument
            {
              ThreadId = dbThread.Id,
              DocumentId = document.Id
            })
            .ToList();

        dbThread.ThreadDocuments.AddRange(newThreadDocuments);
        await dbContext.SaveChangesAsync();

        _logger.LogInformation("Associated {Count} documents with thread {ThreadId}", selectedDocuments.Count, dbThread.Id);
      }
      catch (DbUpdateException ex)
      {
        _logger.LogError(ex, "Database error while associating documents with thread");
        throw;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error associating documents with thread");
        throw;
      }
    }

    public async Task<List<string>> GetThreadDocumentContentsAsync(DuetThread thread)
    {
      if (thread == null || thread.ThreadDocuments == null || !thread.ThreadDocuments.Any())
      {
        _logger.LogInformation("No documents associated with the current thread.");
        return new List<string>();
      }

      await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
      var documentIds = thread.ThreadDocuments.Select(td => td.DocumentId).ToList();
      var documents = await dbContext.Documents
          .Where(d => documentIds.Contains(d.Id))
          .ToListAsync();

      var documentContents = new List<string>();

      foreach (var document in documents)
      {
        string plainText = string.Empty;

        if (document.ContentType == "application/pdf")
        {
          plainText = ExtractTextFromPdf(document.Content);
        }
        else if (document.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
          plainText = ExtractTextFromDocx(document.Content);
        }
        else if (document.ContentType == "application/msword")
        {
          plainText = ExtractTextFromDoc(document.Content);
        }
        else if (document.ContentType == "text/plain" || document.ContentType == "application/octet-stream" ||
             document.ContentType == "text/json" || document.ContentType == "text/xml")
        {
          plainText = System.Text.Encoding.UTF8.GetString(document.Content);
        }
        documentContents.Add("Documentname: " + document.FileName + " " + plainText);
      }

      _logger.LogInformation("Retrieved and converted content for {Count} documents associated with the current thread.", documentContents.Count);

      return documentContents;
    }

    public async Task UpdateThreadMetricsAsync(DuetThread thread, int tokens, decimal cost)
    {
      await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
      try
      {
        var dbThread = await dbContext.Threads.FindAsync(thread.Id);
        if (dbThread != null)
        {
          dbThread.TotalTokens = tokens;
          dbThread.Cost = cost;
          await dbContext.SaveChangesAsync();

          // Update the passed-in thread reference
          thread.TotalTokens = tokens;
          thread.Cost = cost;

          _logger.LogInformation("Updated metrics for thread {ThreadId}: Tokens={Tokens}, Cost={Cost}",
              thread.Id, tokens, cost);
        }
      }
      catch (DbUpdateException ex)
      {
        _logger.LogError(ex, "Database error while updating thread metrics");
        throw;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error updating thread metrics");
        throw;
      }
    }

    // Private helper methods for document text extraction
    private string ExtractTextFromPdf(byte[] pdfContent)
    {
      using (var pdfDocumentProcessor = new PdfDocumentProcessor())
      using (var stream = new MemoryStream(pdfContent))
      {
        pdfDocumentProcessor.LoadDocument(stream);
        var text = new StringBuilder();

        for (int i = 0; i < pdfDocumentProcessor.Document.Pages.Count; i++)
        {
          text.Append(pdfDocumentProcessor.GetPageText(i));
        }

        return text.ToString();
      }
    }

    private string ExtractTextFromDocx(byte[] docxContent)
    {
      using var richEditDocumentServer = new RichEditDocumentServer();
      richEditDocumentServer.LoadDocument(docxContent, DocumentFormat.OpenXml);
      return richEditDocumentServer.Text;
    }

    private string ExtractTextFromDoc(byte[] docContent)
    {
      using var richEditDocumentServer = new RichEditDocumentServer();
      richEditDocumentServer.LoadDocument(docContent, DocumentFormat.Doc);
      return richEditDocumentServer.Text;
    }
  }
}
