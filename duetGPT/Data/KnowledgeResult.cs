#nullable enable
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace duetGPT.Data
{
  /// <summary>
  /// Represents a knowledge result with content and its relevance distance.
  /// This is a non-persisted entity used for query results.
  /// </summary>
  [NotMapped]
  public class KnowledgeResult
  {
    /// <summary>
    /// Gets or sets the content of the knowledge result.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the distance value indicating the relevance of the content.
    /// Lower values indicate higher relevance.
    /// </summary>
    public float Distance { get; set; }
  }
}
