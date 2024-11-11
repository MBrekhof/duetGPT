#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace duetGPT.Data
{
  public class DuetMessage
  {
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    public int ThreadId { get; set; }

    [ForeignKey("ThreadId")]
    public DuetThread? Thread { get; set; }

    [Required]
    public string Role { get; set; }

    [Required]
    public string Content { get; set; }

    public DateTime Created { get; set; } = DateTime.UtcNow;

    public int TokenCount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal MessageCost { get; set; }
  }
}
