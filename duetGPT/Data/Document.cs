using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace duetGPT.Data
{
    public class Document
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public required byte[] Content { get; set; }
        [Required]
        public required string FileName { get; set; }
        [Required]
        public required string ContentType { get; set; }
        public string Description { get; set; } = "<give a description>";
        public DateTime UploadedAt { get; set; }

        public bool General { get; set; } = false;

        [ForeignKey("OwnerId")]
        public ApplicationUser Owner { get; set; }
        public string OwnerId { get; set; }
    }
}