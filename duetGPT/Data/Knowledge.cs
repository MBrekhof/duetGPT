using Pgvector;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace duetGPT.Data
{
    [Table("ragdata")]
    public class Knowledge
    {
        [Key]
        public int ragdataid { get; set; }

        public string? ragcontent { get; set; }

        public int tokens { get; set; }

        [Column(TypeName = "vector(1536)")]
        public Vector? vectordatastring { get; set; }
    }
}
