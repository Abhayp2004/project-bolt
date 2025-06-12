using Microsoft.AspNetCore.Mvc;
using PDFCommenter.Models;
using PDFCommenter.Data;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PDFCommenter.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CommentsController : ControllerBase
    {
        private readonly PDFCommenter.Data.DatabaseService _databaseService;

        public CommentsController(PDFCommenter.Data.DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        [HttpPost]
        public async Task<IActionResult> AddComment([FromBody] CommentRequest request)
        {
            try
            {
                var comment = new Comment
                {
                    
                    PageNumber = request.PageNumber,
                    XPosition = request.XPosition,
                    YPosition = request.YPosition,
                    CommentText = request.CommentText,
                    CreatedBy = User.Identity?.Name ?? "anonymous",
                    CreatedAt = DateTime.UtcNow
                };

                var commentId = await _databaseService.AddCommentAsync(comment);
                return Ok(new { id = commentId });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("{documentId}")]
        public async Task<IActionResult> GetComments(int documentId)
        {
            try
            {
                var comments = await _databaseService.GetCommentsForDocumentAsync(documentId);
                return Ok(comments);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }

    public class CommentRequest
    {
        public int FileId { get; set; }
        public int PageNumber { get; set; }
        public float XPosition { get; set; }
        public float YPosition { get; set; }
        public string CommentText { get; set; }
    }
} 