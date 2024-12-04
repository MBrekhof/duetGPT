#nullable enable
using Pgvector;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace duetGPT.Data
{
    [Table("ragdata")]
    public class Knowledge
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("ragdataid")]
        public int RagDataId { get; set; }

        [Column("ragcontent")]
        public string? RagContent { get; set; }

        [Column("tokens")]
        public int Tokens { get; set; }

        [Column("vectordatastring", TypeName = "vector(1536)")]
        public Vector? VectorDataString { get; set; }

        [StringLength(50)]
        [Column("title")]
        public string? Title { get; set; }

        [Column("creationdate")]
        public DateTime? CreationDate { get; set; } = DateTime.UtcNow;

        [ForeignKey("OwnerId")]
        public ApplicationUser? Owner { get; set; }

        [Column("ownerid")]
        public string? OwnerId { get; set; }
    }
}
