#nullable enable
using Microsoft.EntityFrameworkCore;
using Pgvector;
using System.ComponentModel.DataAnnotations.Schema;

namespace duetGPT.Data
{
  /// <summary>
  /// Represents a raw query result from the knowledge database.
  /// This is a non-persisted entity that matches the SQL query structure.
  /// </summary>
  [NotMapped]
  public class KnowledgeQueryResult
  {
    /// <summary>
    /// Gets or sets the identifier of the knowledge entry.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the RAG (Retrieval-Augmented Generation) content.
    /// </summary>
    public string Ragcontent { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the vector data string representation.
    /// </summary>
    public Vector? Vectordatastring { get; set; }

    /// <summary>
    /// Gets or sets the distance value from the query vector.
    /// Lower values indicate higher relevance.
    /// Note: Lowercase to match SQL alias.
    /// </summary>
    public float distance { get; set; }

    /// <summary>
    /// Gets or sets the metadata associated with the knowledge entry.
    /// Contains structured information about the content type, importance, and key phrases.
    /// </summary>
    public string? Metadata { get; set; }
  }
}
