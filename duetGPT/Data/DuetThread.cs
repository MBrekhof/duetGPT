using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace duetGPT.Data
{
    public class DuetThread
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; }

        [ForeignKey("UserId")]
        public ApplicationUser User { get; set; }

        [Required]
        public DateTime StartTime { get; set; }

        public int TotalTokens { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Cost { get; set; }

        public List<ThreadDocument> ThreadDocuments { get; set; }
    }

    public class ThreadDocument
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ThreadId { get; set; }

        [ForeignKey("ThreadId")]
        public DuetThread Thread { get; set; }

        [Required]
        public int DocumentId { get; set; }

        [ForeignKey("DocumentId")]
        public Document Document { get; set; }
    }
}