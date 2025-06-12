using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PDFCommenter.Data;
using PDFCommenter.Models;
using PDFCommenter.Services;

namespace PDFCommenter.Pages.Documents
{
    public class ViewModel : PageModel
    {
        private readonly DatabaseService _databaseService;
        private readonly DocumentService _documentService;

        public Document Document { get; private set; }
        public List<Comment> Comments { get; private set; }

        [BindProperty]
        public string CommentText { get; set; }
        [BindProperty]
        public int FileId { get; set; }
        [BindProperty]
        public int PageNumber { get; set; } = 1;

        public ViewModel(DatabaseService databaseService, DocumentService documentService)
        {
            _databaseService = databaseService;
            _documentService = documentService;
        }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            Document = await _databaseService.GetDocumentByIdAsync(id);
            if (Document == null)
            {
                return NotFound();
            }

            Comments = await _databaseService.GetCommentsForDocumentAsync(id);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            Document = await _databaseService.GetDocumentByIdAsync(id);
            if (Document == null)
            {
                return NotFound();
            }

            if (!string.IsNullOrWhiteSpace(CommentText))
            {
                var comment = new Comment
                {
                    DocumentId = id,
                    PageNumber = PageNumber,
                    XPosition = 0, // You can enhance this later
                    YPosition = 0,
                    CommentText = CommentText,
                    CreatedBy = User.Identity?.Name ?? "anonymous",
                    CreatedAt = System.DateTime.Now
                };
                await _databaseService.AddCommentAsync(comment);
            }

            Comments = await _databaseService.GetCommentsForDocumentAsync(id);
            CommentText = string.Empty;
            return Page();
        }

        public async Task<IActionResult> OnPostCommentAsync(int id, [FromForm] string CommentText, [FromForm] int PageNumber, [FromForm] float XPosition, [FromForm] float YPosition)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(CommentText))
                {
                    return new JsonResult(new { success = false, message = "Comment text is required" });
                }

                var comment = new Comment
                {
                    DocumentId = id,
                    PageNumber = PageNumber,
                    XPosition = XPosition,
                    YPosition = YPosition,
                    CommentText = CommentText,
                    CreatedBy = User.Identity?.Name ?? "anonymous",
                    CreatedAt = System.DateTime.Now
                };

                var commentId = await _databaseService.AddCommentAsync(comment);
                
                return new JsonResult(new { 
                    success = true, 
                    message = "Comment added successfully",
                    commentId = commentId,
                    createdBy = comment.CreatedBy,
                    createdAt = comment.CreatedAt.ToString("MMM dd, yyyy HH:mm")
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }
    }
}