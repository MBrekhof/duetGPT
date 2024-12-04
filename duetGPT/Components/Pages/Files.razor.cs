using Microsoft.AspNetCore.Components;
using duetGPT.Data;
using duetGPT.Services;
using Microsoft.EntityFrameworkCore;
using System.Text;
using DevExpress.XtraRichEdit;
using DevExpress.Pdf;
using DevExpress.Blazor;
using Microsoft.Extensions.Logging;

namespace duetGPT.Components.Pages;

public partial class Files : ComponentBase
{
  [Inject] private ErrorPopupService ErrorPopupService { get; set; } = default!;
  [Inject] private ILogger<Files> _logger { get; set; } = default!;

  public class DocumentViewModel
  {
    public int Id { get; set; }
    public required string FileName { get; set; }
    public long FileSize { get; set; }
    public DateTime UploadedAt { get; set; }
    public required string ContentType { get; set; }
    public bool General { get; set; }
    public string? OwnerName { get; set; }
  }

  protected IGrid? Grid { get; set; }
  protected IList<DocumentViewModel> Documents { get; set; } = new List<DocumentViewModel>();
  private bool isDeleteConfirmationVisible = false;
  private int recordIdToDelete;
  private int counter = 0;
  private int? currentlyEmbeddingId = null;
  private Dictionary<int, bool> embeddingSuccess = new Dictionary<int, bool>();

  private List<string> SplitArticleIntoChunks(string articleText, int tokenLimit)
  {
    try
    {
      _logger.LogInformation($"Starting to split article into chunks with token limit: {tokenLimit}");

      // Remove line breaks
      articleText = articleText.Replace("\r\n", "");

      // Split the article text by markers
      string markerStart = "<#>";
      string[] sections = articleText.Split(new[] { markerStart }, StringSplitOptions.None);

      // Initialize a list to hold the chunks
      List<string> chunks = new List<string>();

      // Initialize a StringBuilder to build each chunk
      StringBuilder chunk = new StringBuilder();

      // Initialize a counter to keep track of the number of tokens in the current chunk
      int tokenCount = 0;

      // Iterate over the sections
      foreach (string section in sections)
      {
        if (section.Contains(markerStart))
        {
          string markedSection = section.Replace(markerStart, "");
          string[] tokens = markedSection.Split(' ');
          if (tokenCount + tokens.Length > tokenLimit)
          {
            chunks.Add(chunk.ToString());
            chunk.Clear();
            tokenCount = 0;
          }
          chunk.Append(markedSection);
          tokenCount += tokens.Length;
        }
        else
        {
          string[] sentences = section.Split(new[] { ". " }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
          foreach (string sentence in sentences)
          {
            string[] tokens = sentence.Split(' ');
            if (tokens.Length < 2) continue;
            if (tokenCount + tokens.Length > tokenLimit)
            {
              chunks.Add(chunk.ToString());
              chunk.Clear();
              chunk.Append(sentence + ". ");
              tokenCount = tokens.Length;
            }
            else
            {
              chunk.Append(sentence + ". ");
              tokenCount += tokens.Length;
            }
          }
        }
      }

      // Add the last chunk to the list, if it's not empty
      if (chunk.Length > 0)
      {
        chunks.Add(chunk.ToString());
      }

      _logger.LogInformation($"Successfully split article into {chunks.Count} chunks");
      return chunks;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error splitting article into chunks");
      throw new Exception("Failed to split article into chunks", ex);
    }
  }

  private string ExtractTextFromPdf(byte[] content)
  {
    try
    {
      _logger.LogInformation("Starting PDF text extraction");
      using (var pdfDocumentProcessor = new PdfDocumentProcessor())
      using (var stream = new MemoryStream(content))
      {
        pdfDocumentProcessor.LoadDocument(stream);
        var text = new StringBuilder();

        for (int i = 0; i < pdfDocumentProcessor.Document.Pages.Count; i++)
        {
          text.Append(pdfDocumentProcessor.GetPageText(i));
        }

        _logger.LogInformation($"Successfully extracted text from PDF with {pdfDocumentProcessor.Document.Pages.Count} pages");
        return text.ToString();
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error extracting text from PDF");
      throw new Exception("Failed to extract text from PDF", ex);
    }
  }

  private string ExtractTextFromDocx(byte[] content)
  {
    try
    {
      _logger.LogInformation("Starting DOCX text extraction");
      using var richEditDocumentServer = new RichEditDocumentServer();
      richEditDocumentServer.LoadDocument(content, DocumentFormat.OpenXml);
      _logger.LogInformation("Successfully extracted text from DOCX");
      return richEditDocumentServer.Text;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error extracting text from DOCX");
      throw new Exception("Failed to extract text from DOCX", ex);
    }
  }

  private string ExtractTextFromDoc(byte[] content)
  {
    try
    {
      _logger.LogInformation("Starting DOC text extraction");
      using var richEditDocumentServer = new RichEditDocumentServer();
      richEditDocumentServer.LoadDocument(content, DocumentFormat.Doc);
      _logger.LogInformation("Successfully extracted text from DOC");
      return richEditDocumentServer.Text;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error extracting text from DOC");
      throw new Exception("Failed to extract text from DOC", ex);
    }
  }

  protected async Task Embed_Click(DocumentViewModel dataItem)
  {
    try
    {
      _logger.LogInformation($"Starting embedding process for document ID: {dataItem.Id}");
      currentlyEmbeddingId = dataItem.Id;
      embeddingSuccess.Remove(dataItem.Id);
      StateHasChanged();

      var document = await DbContext.Documents.FindAsync(dataItem.Id);
      if (document != null)
      {
        string plainText = string.Empty;

        _logger.LogInformation($"Extracting text from document with type: {document.ContentType}");
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
          plainText = Encoding.UTF8.GetString(document.Content);
        }

        // Split the text into chunks
        var chunks = SplitArticleIntoChunks(plainText, 500);

        _logger.LogInformation($"Creating knowledge records for {chunks.Count} chunks");
        // Create Knowledge records for each chunk
        for (int i = 0; i < chunks.Count; i++)
        {
          var chunk = chunks[i];
          var wordCount = chunk.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

          var knowledge = new Knowledge
          {
              Title = $"#{i + 1}_{document.FileName}"[..Math.Min(50, $"#{i + 1}_{document.FileName}".Length)],
            RagContent = chunk,
            Tokens = wordCount,
            CreationDate = DateTime.UtcNow,
            VectorDataString = await OpenAIService.GetVectorDataAsync(chunk)
          };

          DbContext.Set<Knowledge>().Add(knowledge);
        }

        await DbContext.SaveChangesAsync();
        embeddingSuccess[dataItem.Id] = true;
        _logger.LogInformation($"Successfully created {chunks.Count} knowledge records from document: {document.FileName}");
        ErrorPopupService.ShowError($"Successfully processed document: {document.FileName}");
      }
      else
      {
        _logger.LogWarning($"Document with ID {dataItem.Id} not found");
        ErrorPopupService.ShowError("Document not found");
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, $"Error processing document ID {dataItem.Id} for embedding");
      embeddingSuccess[dataItem.Id] = false;
      ErrorPopupService.ShowError($"Error processing document: {ex.Message}");
    }
    finally
    {
      currentlyEmbeddingId = null;
      StateHasChanged();
    }
  }

  protected async Task Delete_Click(DocumentViewModel dataItem)
  {
    try
    {
      _logger.LogInformation($"Attempting to delete document ID: {dataItem.Id}");
      var document = await DbContext.Documents.FindAsync(dataItem.Id);
      if (document != null)
      {
        DbContext.Documents.Remove(document);
        await DbContext.SaveChangesAsync();
        await LoadDocumentsAsync(); // Refresh the grid data
        _logger.LogInformation($"Successfully deleted document: {document.FileName}");
        ErrorPopupService.ShowError($"Successfully deleted document: {document.FileName}");
      }
      else
      {
        _logger.LogWarning($"Document with ID {dataItem.Id} not found for deletion");
        ErrorPopupService.ShowError("Document not found");
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, $"Error deleting document ID {dataItem.Id}");
      ErrorPopupService.ShowError($"Error deleting document: {ex.Message}");
    }
  }

  protected override async Task OnInitializedAsync()
  {
    try
    {
      _logger.LogInformation("Initializing Files component");
      await LoadDocumentsAsync();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error initializing Files component");
      ErrorPopupService.ShowError("Error loading documents");
    }
  }

  private async Task LoadDocumentsAsync()
  {
    try
    {
      _logger.LogInformation("Loading documents");
      var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
      var user = authState.User;
      var currentUser = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

      if (currentUser != null)
      {
        var documents = await DbContext.Documents
            .Include(d => d.Owner)
            .Where(d => d.OwnerId == currentUser)
            .ToListAsync();

        Documents = documents.Select(d => new DocumentViewModel
        {
          Id = d.Id,
          FileName = d.FileName,
          FileSize = d.Content.Length,
          UploadedAt = d.UploadedAt,
          ContentType = d.ContentType,
          General = d.General,
          OwnerName = d.Owner?.UserName ?? "Unknown"
        }).ToList();

        _logger.LogInformation($"Successfully loaded {Documents.Count} documents");
      }
      else
      {
        _logger.LogWarning("No authenticated user found");
        ErrorPopupService.ShowError("Please log in to view documents");
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error loading documents");
      ErrorPopupService.ShowError("Error loading documents");
      throw;
    }
  }
}
