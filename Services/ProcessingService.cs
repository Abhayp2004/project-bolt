using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using PDFCommenter.Data;
using PDFCommenter.Models;

namespace PDFCommenter.Services
{
    public class ProcessingService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly DatabaseService _databaseService;
        private readonly DocumentService _documentService;

        public ProcessingService(
            IWebHostEnvironment environment,
            DatabaseService databaseService,
            DocumentService documentService)
        {
            _environment = environment;
            _databaseService = databaseService;
            _documentService = documentService;
        }

        public async Task ProcessDocumentAsync(Document document)
        {
            try
            {
                // Update status to Processing
                await _databaseService.UpdateDocumentStatusAsync(document.Id, "Processing");

                // Extract text from PDF if needed
                var pdfText = await _documentService.ExtractTextFromPdfAsync(document.PDFPath);

                // Update status to Completed
                await _databaseService.UpdateDocumentStatusAsync(document.Id, "Completed");
            }
            catch (Exception)
            {
                // Update status to Failed
                await _databaseService.UpdateDocumentStatusAsync(document.Id, "Failed");
                throw;
            }
        }
    }
}