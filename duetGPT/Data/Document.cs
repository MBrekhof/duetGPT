using System;
using System.ComponentModel.DataAnnotations;

namespace duetGPT.Data
{
    public class Document
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public byte[] Content { get; set; }
        [Required]
        public string FileName { get; set; }
        [Required]
        public string ContentType { get; set; }
        public string Description { get; set; } = "<give a description>";
        public DateTime UploadedAt { get; set; }
    }
}