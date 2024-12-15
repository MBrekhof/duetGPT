using System.Text;
using System.Text.RegularExpressions;
using DevExpress.Pdf;
using DevExpress.XtraRichEdit;

namespace duetGPT.Services;

public class DocumentProcessingService
{
  private class TextChunk
  {
    public string Content { get; set; } = string.Empty;
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
  }

  public string ExtractTextFromPdf(byte[] content)
  {
    using var pdfDocumentProcessor = new PdfDocumentProcessor();
    using var stream = new MemoryStream(content);
    pdfDocumentProcessor.LoadDocument(stream);
    var text = new StringBuilder();

    for (int i = 0; i < pdfDocumentProcessor.Document.Pages.Count; i++)
    {
      text.Append(pdfDocumentProcessor.GetPageText(i));
    }

    return text.ToString();
  }

  public string ExtractTextFromDocx(byte[] content)
  {
    using var richEditDocumentServer = new RichEditDocumentServer();
    richEditDocumentServer.LoadDocument(content, DocumentFormat.OpenXml);
    return richEditDocumentServer.Text;
  }

  public string ExtractTextFromDoc(byte[] content)
  {
    using var richEditDocumentServer = new RichEditDocumentServer();
    richEditDocumentServer.LoadDocument(content, DocumentFormat.Doc);
    return richEditDocumentServer.Text;
  }

  public List<string> SplitArticleIntoChunks(string articleText, int tokenLimit)
  {
    articleText = NormalizeText(articleText);
    var sections = ExtractStructuralElements(articleText);
    var chunks = CreateOverlappingChunks(sections, tokenLimit);

    return chunks.Select(chunk =>
    {
      var metadata = new StringBuilder();
      foreach (var meta in chunk.Metadata)
      {
        metadata.AppendLine($"[{meta.Key}: {meta.Value}]");
      }
      return $"{metadata}\n{chunk.Content}".Trim();
    }).ToList();
  }

  private string NormalizeText(string text)
  {
    text = Regex.Replace(text, @"\s+", " ");
    text = Regex.Replace(text, @"\n\s*\n", "\n\n");
    text = text.Normalize(NormalizationForm.FormKC);
    text = text.Replace("\r", "\n")
             .Replace("\t", " ")
             .Replace("•", "* ");
    return text.Trim();
  }

  private List<TextChunk> ExtractStructuralElements(string text)
  {
    var chunks = new List<TextChunk>();
    var position = 0;
    var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

    foreach (var paragraph in paragraphs)
    {
      bool isHeader = IsLikelyHeader(paragraph);
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

      if (isHeader)
      {
        chunk.Metadata["importance"] = "high";
      }

      var keyPhrases = ExtractKeyPhrases(paragraph);
      if (keyPhrases.Any())
      {
        chunk.Metadata["key_phrases"] = string.Join(", ", keyPhrases);
      }

      chunks.Add(chunk);
      position += paragraph.Length + 2;
    }

    return chunks;
  }

  private bool IsLikelyHeader(string text)
  {
    if (text.Length > 100) return false;
    if (Regex.IsMatch(text, @"^[A-Z][^.!?]*$")) return true;
    if (Regex.IsMatch(text, @"^\d+(\.\d+)*\s+[A-Z]")) return true;
    if (text.All(c => char.IsUpper(c) || char.IsWhiteSpace(c) || char.IsDigit(c))) return true;
    return false;
  }

  private List<string> ExtractKeyPhrases(string text)
  {
    var keyPhrases = new List<string>();

    var quotes = Regex.Matches(text, @"""([^""]+)""");
    keyPhrases.AddRange(quotes.Select(m => m.Groups[1].Value));

    var specialFormatting = Regex.Matches(text, @"\*([^*]+)\*");
    keyPhrases.AddRange(specialFormatting.Select(m => m.Groups[1].Value));

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
    var overlap = tokenLimit / 5;

    foreach (var element in elements)
    {
      var elementWords = element.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      var elementTokens = elementWords.Length;

      if (elementTokens > tokenLimit)
      {
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

      var currentTokens = currentChunk.Length > 0
          ? currentChunk.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length
          : 0;

      if (currentTokens + elementTokens > tokenLimit)
      {
        if (currentChunk.Length > 0)
        {
          chunks.Add(new TextChunk
          {
            Content = currentChunk.ToString().Trim(),
            Metadata = new Dictionary<string, string>(currentMetadata)
          });

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

      currentChunk.Append(element.Content + " ");

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
}