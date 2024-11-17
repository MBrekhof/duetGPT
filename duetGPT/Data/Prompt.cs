#nullable enable

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace duetGPT.Data;

public class Prompt
{
  [Key]
  [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
  public virtual int PromptID { get; set; }

  [Required]
  [StringLength(50)]
  public virtual string? Title { get; set; }

  [Required]
  public virtual string? Content { get; set; }

  [Required]
  [StringLength(20)]
  public virtual string? Name { get; set; }
}
