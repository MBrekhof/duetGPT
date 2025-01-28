using Anthropic.SDK.Messaging;
using DevExpress.Blazor;
using DevExpress.Pdf;
using DevExpress.XtraRichEdit;
using duetGPT.Data;
using duetGPT.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace duetGPT.Components.Pages;

public partial class Files : ComponentBase
{
  [Inject] private ILogger<Files> _logger { get; set; } = default!;
  [Inject] private AuthenticationStateProvider _authStateProvider { get; set; } = default!;
  [Inject] private IToastNotificationService _toastService { get; set; } = default!;

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
  private int? currentlyEmbeddingId = null;
  private Dictionary<int, bool> embeddingSuccess = new Dictionary<int, bool>();
  private byte[]? selectedPdfContent;
  private bool showPdfViewer;

  private class TextChunk
  {
    public string Content { get; set; } = string.Empty;
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
  }

  private List<string> SplitArticleIntoChunks(string articleText, int tokenLimit)
  {
    try
    {
      _logger.LogInformation($"Starting to split article into chunks with token limit: {tokenLimit}");

      // Clean and normalize the text
      articleText = NormalizeText(articleText);

      // Extract structural elements
      var sections = ExtractStructuralElements(articleText);

      // Create overlapping chunks with metadata
      var chunks = CreateOverlappingChunks(sections, tokenLimit);

      // Convert chunks to final format
      // Keep raw content chunks separate from metadata
      var finalChunks = new List<string>();
      foreach (var chunk in chunks)
      {
        // Use a conservative estimate - assume each word could be 1.5 tokens on average
        var estimatedTokens = (chunk.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length * 3) / 2;

        if (estimatedTokens > 2000) // Keep well under OpenAI's 8192 limit
        {
          // Further split the chunk if it's too large
          var words = chunk.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
          var targetWordCount = 1300; // (~2000 estimated tokens)

          for (int i = 0; i < words.Length; i += targetWordCount)
          {
            var subChunkWords = words.Skip(i).Take(targetWordCount);
            finalChunks.Add(string.Join(" ", subChunkWords));
          }
        }
        else
        {
          finalChunks.Add(chunk.Content);
        }
      }

      _logger.LogInformation($"Successfully split article into {finalChunks.Count} chunks");
      return finalChunks;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error splitting article into chunks");
      throw new Exception("Failed to split article into chunks", ex);
    }
  }

  protected async Task View_Click(DocumentViewModel dataItem)
  {
    try
    {
      _logger.LogInformation($"Attempting to view document ID: {dataItem.Id}");
      var document = await DbContext.Documents.FindAsync(dataItem.Id);

      if (document != null && document.Content != null && document.ContentType == "application/pdf")
      {
        selectedPdfContent = document.Content;
        showPdfViewer = true;
        StateHasChanged();
        _logger.LogInformation($"Successfully loaded PDF for viewing: {document.FileName}");
      }
      else
      {
        _logger.LogWarning($"Document with ID {dataItem.Id} not found or is not a PDF");
        _toastService.ShowToast(new ToastOptions()
        {
          ProviderName = "FilesPage",
          ThemeMode = ToastThemeMode.Saturated,
          RenderStyle = ToastRenderStyle.Warning,
          Title = "Warning",
          Text = "Document not found or is not a PDF file"
        });
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, $"Error viewing document ID {dataItem.Id}");
      _toastService.ShowToast(new ToastOptions()
      {
        ProviderName = "FilesPage",
        ThemeMode = ToastThemeMode.Saturated,
        RenderStyle = ToastRenderStyle.Danger,
        Title = "Error",
        Text = $"Error viewing document: {ex.Message}"
      });
    }
  }

  private string NormalizeText(string text)
  {
    // Remove excessive whitespace while preserving paragraph breaks
    text = Regex.Replace(text, @"\s+", " ");
    text = Regex.Replace(text, @"\n\s*\n", "\n\n");

    // Normalize unicode characters
    text = text.Normalize(NormalizationForm.FormKC);

    // Replace special characters that might interfere with processing
    text = text.Replace("\r", "\n")
               .Replace("\t", " ")
               .Replace("•", "* ");

    return text.Trim();
  }

  private List<TextChunk> ExtractStructuralElements(string text)
  {
    var chunks = new List<TextChunk>();
    var position = 0;

    // Split into paragraphs first
    var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

    foreach (var paragraph in paragraphs)
    {
      // Detect if this is a header
      bool isHeader = IsLikelyHeader(paragraph);

      // Detect if this contains a list
      bool isList = paragraph.Contains("* ") || Regex.IsMatch(paragraph, @"^\d+\.");

      var chunk = new TextChunk
      {
        Content = paragraph.Trim(),
        StartPosition = position,
        EndPosition = position + paragraph.Length,
        Metadata = new Dictionary<string, string>
        {
          { "type", isHeader ? "header" : isList ? "list" : "paragraph" },
          { "length", paragraph.Split(' ').Length.ToString() }
        }
      };

      // Add semantic metadata
      if (isHeader)
      {
        chunk.Metadata["importance"] = "high";
      }

      // Detect potential key phrases
      var keyPhrases = ExtractKeyPhrases(paragraph);
      if (keyPhrases.Any())
      {
        chunk.Metadata["key_phrases"] = string.Join(", ", keyPhrases);
      }

      chunks.Add(chunk);
      position += paragraph.Length + 2; // +2 for the paragraph separator
    }

    return chunks;
  }

  private bool IsLikelyHeader(string text)
  {
    // Headers are typically short
    if (text.Length > 100) return false;

    // Check for common header patterns
    if (Regex.IsMatch(text, @"^[A-Z][^.!?]*$")) return true;
    if (Regex.IsMatch(text, @"^\d+(\.\d+)*\s+[A-Z]")) return true;
    if (text.All(c => char.IsUpper(c) || char.IsWhiteSpace(c) || char.IsDigit(c))) return true;

    return false;
  }

  private List<string> ExtractKeyPhrases(string text)
  {
    var keyPhrases = new List<string>();

    // Look for phrases in quotes
    var quotes = Regex.Matches(text, @"""([^""]+)""");
    keyPhrases.AddRange(quotes.Select(m => m.Groups[1].Value));

    // Look for phrases with special formatting (assuming they were preserved from the original document)
    var specialFormatting = Regex.Matches(text, @"\*([^*]+)\*");
    keyPhrases.AddRange(specialFormatting.Select(m => m.Groups[1].Value));

    // Look for likely key phrases based on common patterns
    var patterns = new[]
    {
      @"(?:is|are|was|were)\s+(?:called|known as|defined as)\s+([^.,;]+)",
      @"(?:important|key|critical|essential|significant)\s+(?:is|are|factor|aspect|element)s?\s+([^.,;]+)",
      @"(?:in conclusion|to summarize|therefore)\s+([^.,;]+)"
    };

    foreach (var pattern in patterns)
    {
      var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase);
      keyPhrases.AddRange(matches.Select(m => m.Groups[1].Value.Trim()));
    }

    return keyPhrases.Distinct().ToList();
  }

  private List<TextChunk> CreateOverlappingChunks(List<TextChunk> elements, int tokenLimit)
  {
    var chunks = new List<TextChunk>();
    var currentChunk = new StringBuilder();
    var currentMetadata = new Dictionary<string, string>();
    var overlap = tokenLimit / 5; // 20% overlap

    foreach (var element in elements)
    {
      var elementWords = element.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      var elementTokens = elementWords.Length;

      // If the element itself is larger than the token limit, split it
      if (elementTokens > tokenLimit)
      {
        // If there's content in the current chunk, add it first
        if (currentChunk.Length > 0)
        {
          chunks.Add(new TextChunk
          {
            Content = currentChunk.ToString().Trim(),
            Metadata = new Dictionary<string, string>(currentMetadata)
          });
          currentChunk.Clear();
          currentMetadata.Clear();
        }

        // Split the large element into smaller chunks
        for (int i = 0; i < elementWords.Length; i += tokenLimit - overlap)
        {
          var chunkWords = elementWords.Skip(i).Take(tokenLimit).ToArray();
          var chunkContent = string.Join(" ", chunkWords);

          chunks.Add(new TextChunk
          {
            Content = chunkContent,
            Metadata = new Dictionary<string, string>(element.Metadata)
          });
        }
        continue;
      }

      // Calculate the current chunk's token count
      var currentTokens = currentChunk.Length > 0
        ? currentChunk.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length
        : 0;

      // Check if adding this element would exceed the token limit
      if (currentTokens + elementTokens > tokenLimit)
      {
        // Store the current chunk
        if (currentChunk.Length > 0)
        {
          chunks.Add(new TextChunk
          {
            Content = currentChunk.ToString().Trim(),
            Metadata = new Dictionary<string, string>(currentMetadata)
          });

          // If this is not a header, keep overlap from previous chunk
          if (element.Metadata["type"] != "header")
          {
            var words = currentChunk.ToString().Split(' ');
            var overlapText = string.Join(" ", words.Skip(Math.Max(0, words.Length - overlap)));
            currentChunk.Clear().Append(overlapText + " ");
            currentTokens = overlap;
          }
          else
          {
            currentChunk.Clear();
            currentTokens = 0;
          }
          currentMetadata.Clear();
        }
      }

      // Add the element to the current chunk
      currentChunk.Append(element.Content + " ");

      // Merge metadata
      foreach (var meta in element.Metadata)
      {
        if (!currentMetadata.ContainsKey(meta.Key))
        {
          currentMetadata[meta.Key] = meta.Value;
        }
        else if (meta.Key == "key_phrases")
        {
          currentMetadata[meta.Key] += ", " + meta.Value;
        }
      }
    }

    // Add the final chunk if there's anything left
    if (currentChunk.Length > 0)
    {
      chunks.Add(new TextChunk
      {
        Content = currentChunk.ToString().Trim(),
        Metadata = new Dictionary<string, string>(currentMetadata)
      });
    }

    return chunks;
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

      // Get current user ID
      var authState = await _authStateProvider.GetAuthenticationStateAsync();
      var currentUserId = authState.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

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
        decimal totalEmbeddingCost = 0;
        // Create Knowledge records for each chunk
        for (int i = 0; i < chunks.Count; i++)
        {
          var chunk = chunks[i];
          var wordCount = chunk.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

          // Store metadata separately from the content that gets embedded
          var metadata = new StringBuilder();
          if (i < chunks.Count - 1)
          {
            metadata.AppendLine($"[section: {i + 1} of {chunks.Count}]");
            metadata.AppendLine($"[document: {document.FileName}]");
            if (document.ContentType == "application/pdf")
            {
              metadata.AppendLine("[type: PDF]");
            }
            else if (document.ContentType.Contains("word"))
            {
              metadata.AppendLine("[type: Word Document]");
            }
          }

          // Get embedding with cost tracking
          var embeddingResult = await OpenAIService.GetVectorDataAsync(chunk);
          totalEmbeddingCost += embeddingResult.Cost;

          var knowledge = new Knowledge
          {
            Title = $"#{i + 1}_{document.FileName}"[..Math.Min(50, $"#{i + 1}_{document.FileName}".Length)],
            RagContent = chunk,
            Metadata = metadata.ToString(),
            Tokens = embeddingResult.TokenCount,
            CreationDate = DateTime.UtcNow,
            VectorDataString = embeddingResult.Vector,
            OwnerId = currentUserId,
            EmbeddingCost = embeddingResult.Cost
          };

          DbContext.Set<Knowledge>().Add(knowledge);
        }

        await DbContext.SaveChangesAsync();
        embeddingSuccess[dataItem.Id] = true;
        _logger.LogInformation($"Successfully created {chunks.Count} knowledge records from document: {document.FileName}. Total embedding cost: ${totalEmbeddingCost:F6}");
        _toastService.ShowToast(new ToastOptions()
        {
          ProviderName = "FilesPage",
          ThemeMode = ToastThemeMode.Saturated,
          RenderStyle = ToastRenderStyle.Success,
          Title = "Success",
          Text = $"Successfully processed document: {document.FileName}"
        });
      }
      else
      {
        _logger.LogWarning($"Document with ID {dataItem.Id} not found");
        _toastService.ShowToast(new ToastOptions()
        {
          ProviderName = "FilesPage",
          ThemeMode = ToastThemeMode.Saturated,
          RenderStyle = ToastRenderStyle.Warning,
          Title = "Warning",
          Text = "Document not found"
        });
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, $"Error processing document ID {dataItem.Id} for embedding");
      embeddingSuccess[dataItem.Id] = false;
      _toastService.ShowToast(new ToastOptions()
      {
        ProviderName = "FilesPage",
        ThemeMode = ToastThemeMode.Saturated,
        RenderStyle = ToastRenderStyle.Danger,
        Title = "Error",
        Text = $"Error processing document: {ex.Message}"
      });
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
        _toastService.ShowToast(new ToastOptions()
        {
          ProviderName = "FilesPage",
          ThemeMode = ToastThemeMode.Saturated,
          RenderStyle = ToastRenderStyle.Success,
          Title = "Success",
          Text = $"Successfully deleted document: {document.FileName}"
        });
      }
      else
      {
        _logger.LogWarning($"Document with ID {dataItem.Id} not found for deletion");
        _toastService.ShowToast(new ToastOptions()
        {
          ProviderName = "FilesPage",
          ThemeMode = ToastThemeMode.Saturated,
          RenderStyle = ToastRenderStyle.Warning,
          Title = "Warning",
          Text = "Document not found"
        });
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, $"Error deleting document ID {dataItem.Id}");
      _toastService.ShowToast(new ToastOptions()
      {
        ProviderName = "FilesPage",
        ThemeMode = ToastThemeMode.Saturated,
        RenderStyle = ToastRenderStyle.Danger,
        Title = "Error",
        Text = $"Error deleting document: {ex.Message}"
      });
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
      _toastService.ShowToast(new ToastOptions()
      {
        ProviderName = "FilesPage",
        ThemeMode = ToastThemeMode.Saturated,
        RenderStyle = ToastRenderStyle.Danger,
        Title = "Error",
        Text = "Error loading documents"
      });
    }
  }

  private async Task LoadDocumentsAsync()
  {
    try
    {
      _logger.LogInformation("Loading documents");
      var authState = await _authStateProvider.GetAuthenticationStateAsync();
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
        _toastService.ShowToast(new ToastOptions()
        {
          ProviderName = "FilesPage",
          ThemeMode = ToastThemeMode.Saturated,
          RenderStyle = ToastRenderStyle.Warning,
          Title = "Authentication Required",
          Text = "Please log in to view documents"
        });
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error loading documents");
      _toastService.ShowToast(new ToastOptions()
      {
        ProviderName = "FilesPage",
        ThemeMode = ToastThemeMode.Saturated,
        RenderStyle = ToastRenderStyle.Danger,
        Title = "Error",
        Text = "Error loading documents"
      });
      throw;
    }
  }
}
