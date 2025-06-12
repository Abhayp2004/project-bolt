using System;

namespace PDFCommenter.Models
{
    public class Comment
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public int PageNumber { get; set; }
        public float XPosition { get; set; }
        public float YPosition { get; set; }
        public string CommentText { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        
        // Navigation property
        public Document Document { get; set; }
    }
}