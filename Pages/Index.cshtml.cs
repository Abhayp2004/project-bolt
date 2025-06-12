using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using PDFCommenter.Data;
using PDFCommenter.Models;
using PDFCommenter.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PDFCommenter.Pages
{
    public class IndexModel : PageModel
    {
        private readonly DocumentService _documentService;
        private readonly DatabaseService _databaseService;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(
            DocumentService documentService,
            DatabaseService databaseService,
            ILogger<IndexModel> logger)
        {
            _documentService = documentService;
            _databaseService = databaseService;
            _logger = logger;
        }

        // Binds the uploaded file from the form to this property
        [BindProperty]
        public IFormFile PDFFile { get; set; }

        // Binds the uploaded Excel file from the form to this property
        [BindProperty]
        public IFormFile ExcelFile { get; set; }

        // Binds the zoom factor from the form
        [BindProperty]
        public double ZoomFactor { get; set; } = 1.0;

        // Holds the list of documents to display in the table
        public List<Document> Documents { get; set; } = new List<Document>();

        // Displays success or error messages to the user
        [TempData]
        public string StatusMessage { get; set; }

        public string Filter { get; set; }

        // Handles the initial page load (GET request)
        public async Task OnGetAsync()
        {
            await LoadDocumentsAsync();
        }

        // Handles the form submission (POST request)
        public async Task<IActionResult> OnPostAsync()
        {
            if ((PDFFile == null || PDFFile.Length == 0) && (ExcelFile == null || ExcelFile.Length == 0))
            {
                StatusMessage = "Error: Please select a PDF or Excel file to upload.";
                await LoadDocumentsAsync();
                return Page();
            }

            try
            {
                var username = User.Identity?.Name ?? "anonymous";
                if (PDFFile != null && PDFFile.Length > 0)
                {
                    var document = await _documentService.UploadDocumentAsync(PDFFile, username);
                    StatusMessage = $"PDF file '{document.PDFName}' uploaded successfully!";
                }
                else if (ExcelFile != null && ExcelFile.Length > 0)
                {
                    var document = await _documentService.UploadDocumentAsync(ExcelFile, username);
                    StatusMessage = $"Excel file '{document.ExcelName}' uploaded successfully!";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: Upload failed. {ex.Message}";
                _logger.LogError(ex, "Upload failed for file {FileName}", PDFFile?.FileName ?? ExcelFile?.FileName);
            }

            return RedirectToPage();
        }

        // Helper method to load the list of documents from the database
        private async Task LoadDocumentsAsync()
        {
            try
            {
                Documents = await _databaseService.GetDocumentsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading documents.");
                StatusMessage = "Error: Could not load the document list. " + ex.Message;
            }
        }
    }
}
