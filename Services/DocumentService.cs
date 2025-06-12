using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PDFCommenter.Data;
using PDFCommenter.Models;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using OfficeOpenXml;
using System.Data;

namespace PDFCommenter.Services
{
    public class DocumentService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly DatabaseService _databaseService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DocumentService> _logger;

        public DocumentService(
            IWebHostEnvironment environment,
            DatabaseService databaseService,
            IConfiguration configuration,
            ILogger<DocumentService> logger)
        {
            _environment = environment;
            _databaseService = databaseService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<Document> UploadDocumentAsync(IFormFile file, string username)
        {
            try
            {
                _logger.LogInformation("Starting file upload for {FileName}", file.FileName);

                // Validate file
                ValidateFile(file);

                // Determine file extension and target directory
                var fileName = Path.GetFileName(file.FileName);
                var extension = Path.GetExtension(fileName).ToLowerInvariant();
                string subfolder = extension == ".pdf" ? "pdfs" : "excel";
                var uploadsDir = Path.Combine(_environment.WebRootPath, "uploads", subfolder);
                Directory.CreateDirectory(uploadsDir);

                // Save file
                var filePath = Path.Combine("uploads", subfolder, $"{Guid.NewGuid()}_{fileName}");
                var fullPath = Path.Combine(_environment.WebRootPath, filePath);

                _logger.LogInformation("Saving file to {FilePath}", fullPath);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // Verify file was saved
                if (!File.Exists(fullPath))
                {
                    throw new IOException($"Failed to save file to {fullPath}");
                }

                // Calculate file size in MB
                float fileSizeMB = file.Length / (1024f * 1024f);

                // Create document record based on file type
                var document = new Document
                {
                    UploadedBy = username ?? "anonymous",
                    FileSizeMB = fileSizeMB,
                    Status = "Pending"
                };

                if (extension == ".pdf")
                {
                    // PDF file
                    int? pageCount = await GetPDFPageCountAsync(fullPath);
                    _logger.LogInformation("PDF has {PageCount} pages", pageCount);

                    document.PDFName = fileName;
                    document.PDFPath = filePath;
                    document.PDFPages = pageCount;
                    document.ExcelName = "";
                    document.ExcelPath = "";
                    document.ExcelSizeMB = 0;
                }
                else if (extension == ".xls" || extension == ".xlsx")
                {
                    // Excel file
                    document.PDFName = "";
                    document.PDFPath = "";
                    document.PDFPages = null;
                    document.ExcelName = fileName;
                    document.ExcelPath = filePath;
                    document.ExcelSizeMB = fileSizeMB;
                }

                _logger.LogInformation("Creating database record for document");
                document.Id = await _databaseService.CreateDocumentAsync(document);
                _logger.LogInformation("Document uploaded successfully with ID {DocumentId}", document.Id);

                return document;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file {FileName}", file?.FileName);
                throw new Exception($"Failed to upload file: {ex.Message}", ex);
            }
        }

        private void ValidateFile(IFormFile file)
        {
            if (file == null)
                throw new ArgumentException("File is required");

            if (file.Length == 0)
                throw new ArgumentException("File is empty");

            // Get configuration settings
            int maxSizeMB = _configuration.GetValue<int>("FileSettings:MaxSizeInMB", 100); // Default to 100MB
            var allowedExtensions = _configuration.GetSection("FileSettings:AllowedExtensions").Get<string[]>() ?? new[] { ".pdf" };

            _logger.LogInformation("Validating file {FileName} with size {FileSize}MB", file.FileName, file.Length / (1024f * 1024f));

            // Check file size
            if (file.Length > maxSizeMB * 1024 * 1024)
                throw new ArgumentException($"File size exceeds the maximum allowed size of {maxSizeMB}MB");

            // Check file extension
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!Array.Exists(allowedExtensions, ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Invalid file type. Allowed types are: {string.Join(", ", allowedExtensions)}");

            _logger.LogInformation("File validation passed");
        }

        private async Task<int?> GetPDFPageCountAsync(string filePath)
        {
            try
            {
                // Use Task.Run for CPU-bound PDF operations
                return await Task.Run(() =>
                {
                    using (var pdfReader = new PdfReader(filePath))
                    using (var pdfDocument = new PdfDocument(pdfReader))
                    {
                        return pdfDocument.GetNumberOfPages();
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting page count for PDF {FilePath}", filePath);
                // If there's an error reading the PDF, return null
                return null;
            }
        }

        public async Task<string> ExtractTextFromPdfAsync(string pdfPath)
        {
            var fullPath = Path.Combine(_environment.WebRootPath, pdfPath);
            var text = new StringWriter();

            try
            {
                // Use Task.Run for CPU-bound PDF operations
                await Task.Run(async () =>
                {
                    using (var pdfReader = new PdfReader(fullPath))
                    using (var pdfDocument = new PdfDocument(pdfReader))
                    {
                        for (int i = 1; i <= pdfDocument.GetNumberOfPages(); i++)
                        {
                            var pageText = PdfTextExtractor.GetTextFromPage(pdfDocument.GetPage(i), new SimpleTextExtractionStrategy());
                            await text.WriteLineAsync($"--- Page {i} ---");
                            await text.WriteLineAsync(pageText);
                            await text.WriteLineAsync();
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text from PDF {FilePath}", pdfPath);
                await text.WriteLineAsync($"Error extracting text: {ex.Message}");
            }

            return text.ToString();
        }

        public async Task<DataTable> ExtractDataFromExcelAsync(string excelPath)
        {
            var fullPath = Path.Combine(_environment.WebRootPath, excelPath);
            var dataTable = new DataTable();

            try
            {
                // Use Task.Run for CPU-bound Excel operations
                await Task.Run(() =>
                {
                    ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                    
                    using (var package = new ExcelPackage(new FileInfo(fullPath)))
                    {
                        var worksheet = package.Workbook.Worksheets[0]; // Get first worksheet
                        if (worksheet != null)
                        {
                            var startRow = worksheet.Dimension.Start.Row;
                            var endRow = worksheet.Dimension.End.Row;
                            var startCol = worksheet.Dimension.Start.Column;
                            var endCol = worksheet.Dimension.End.Column;

                            // Add columns
                            for (int col = startCol; col <= endCol; col++)
                            {
                                var columnName = worksheet.Cells[startRow, col].Text;
                                if (string.IsNullOrEmpty(columnName))
                                    columnName = $"Column{col}";
                                dataTable.Columns.Add(columnName);
                            }

                            // Add rows
                            for (int row = startRow + 1; row <= endRow; row++)
                            {
                                var dataRow = dataTable.NewRow();
                                for (int col = startCol; col <= endCol; col++)
                                {
                                    dataRow[col - startCol] = worksheet.Cells[row, col].Text;
                                }
                                dataTable.Rows.Add(dataRow);
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting data from Excel {FilePath}", excelPath);
                throw new Exception($"Error reading Excel file: {ex.Message}", ex);
            }

            return dataTable;
        }

        public async Task<string> GetExcelFileInfoAsync(string excelPath)
        {
            var fullPath = Path.Combine(_environment.WebRootPath, excelPath);
            var info = new StringWriter();

            try
            {
                await Task.Run(() =>
                {
                    ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                    
                    using (var package = new ExcelPackage(new FileInfo(fullPath)))
                    {
                        info.WriteLine($"Workbook: {package.File.Name}");
                        info.WriteLine($"Worksheets: {package.Workbook.Worksheets.Count}");
                        info.WriteLine();

                        foreach (var worksheet in package.Workbook.Worksheets)
                        {
                            info.WriteLine($"Worksheet: {worksheet.Name}");
                            info.WriteLine($"Dimensions: {worksheet.Dimension?.Address ?? "Empty"}");
                            info.WriteLine($"Rows: {worksheet.Dimension?.Rows ?? 0}");
                            info.WriteLine($"Columns: {worksheet.Dimension?.Columns ?? 0}");
                            info.WriteLine();
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Excel file info {FilePath}", excelPath);
                info.WriteLine($"Error reading Excel file: {ex.Message}");
            }

            return info.ToString();
        }
    }
}