using Microsoft.AspNetCore.Components;
using duetGPT.Data;
using duetGPT.Services;
using Microsoft.EntityFrameworkCore;
using System.Text;
using DevExpress.XtraRichEdit;
using DevExpress.Pdf;
using DevExpress.Blazor;

namespace duetGPT.Components.Pages;

public partial class Files : ComponentBase
{
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

    return chunks;
  }

  private string ExtractTextFromPdf(byte[] content)
  {
    using (var pdfDocumentProcessor = new PdfDocumentProcessor())
    using (var stream = new MemoryStream(content))
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

  private string ExtractTextFromDocx(byte[] content)
  {
    using var richEditDocumentServer = new RichEditDocumentServer();
    richEditDocumentServer.LoadDocument(content, DocumentFormat.OpenXml);
    return richEditDocumentServer.Text;
  }

  private string ExtractTextFromDoc(byte[] content)
  {
    using var richEditDocumentServer = new RichEditDocumentServer();
    richEditDocumentServer.LoadDocument(content, DocumentFormat.Doc);
    return richEditDocumentServer.Text;
  }

  protected async Task Embed_Click(DocumentViewModel dataItem)
  {
    try
    {
      currentlyEmbeddingId = dataItem.Id;
      embeddingSuccess.Remove(dataItem.Id);
      StateHasChanged();

      var document = await DbContext.Documents.FindAsync(dataItem.Id);
      if (document != null)
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
          plainText = Encoding.UTF8.GetString(document.Content);
        }

        // Split the text into chunks
        var chunks = SplitArticleIntoChunks(plainText, 500);

        // Create Knowledge records for each chunk
        for (int i = 0; i < chunks.Count; i++)
        {
          var chunk = chunks[i];
          var wordCount = chunk.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

          var knowledge = new Knowledge
          {
            Title = $"{document.FileName}_{i + 1}",
            RagContent = chunk,
            Tokens = wordCount,
            CreationDate = DateTime.UtcNow,
            VectorDataString = await OpenAIService.GetVectorDataAsync(chunk)
          };

          DbContext.Set<Knowledge>().Add(knowledge);
        }

        await DbContext.SaveChangesAsync();
        embeddingSuccess[dataItem.Id] = true;
        Logger.LogInformation($"Successfully created {chunks.Count} knowledge records from document: {document.FileName}");
      }
    }
    catch (Exception ex)
    {
      embeddingSuccess[dataItem.Id] = false;
      Logger.LogError(ex, "Error processing document for embedding");
      throw;
    }
    finally
    {
      currentlyEmbeddingId = null;
      StateHasChanged();
    }
  }

  protected async Task Delete_Click(DocumentViewModel dataItem)
  {
    var document = await DbContext.Documents.FindAsync(dataItem.Id);
    if (document != null)
    {
      DbContext.Documents.Remove(document);
      await DbContext.SaveChangesAsync();
      await LoadDocumentsAsync(); // Refresh the grid data
    }
  }

  protected override async Task OnInitializedAsync()
  {
    await LoadDocumentsAsync();
  }

  private async Task LoadDocumentsAsync()
  {
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
    }
  }
}
