using Microsoft.AspNetCore.Mvc;
using PDFCommenter.Models;
using PDFCommenter.Services;
using PDFCommenter.Data;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Data;
using System.Linq;

namespace PDFCommenter.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DocumentsController : ControllerBase
    {
        private readonly DocumentService _documentService;
        private readonly PDFCommenter.Data.DatabaseService _databaseService;

        public DocumentsController(
            DocumentService documentService,
            PDFCommenter.Data.DatabaseService databaseService)
        {
            _documentService = documentService;
            _databaseService = databaseService;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadDocument([FromForm] IFormFile PDFFile, [FromForm] IFormFile ExcelFile, [FromForm] double ZoomFactor)
        {
            try
            {
                IFormFile fileToUpload = null;
                if (PDFFile != null && PDFFile.Length > 0)
                {
                    fileToUpload = PDFFile;
                }
                else if (ExcelFile != null && ExcelFile.Length > 0)
                {
                    fileToUpload = ExcelFile;
                }
                else
                {
                    return BadRequest("Please select a PDF or Excel file to upload.");
                }

                var username = User.Identity?.Name ?? "anonymous";
                var document = await _documentService.UploadDocumentAsync(fileToUpload, username);

                string fileName = !string.IsNullOrEmpty(document.PDFName) ? document.PDFName : document.ExcelName;
                string fileType = !string.IsNullOrEmpty(document.PDFName) ? "PDF" : "Excel";

                return Ok(new {
                    id = document.Id,
                    name = fileName,
                    type = fileType,
                    message = $"{fileType} file uploaded successfully!"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetDocuments()
        {
            try
            {
                var documents = await _databaseService.GetDocumentsAsync();
                return Ok(documents);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("excel/{id}/data")]
        public async Task<IActionResult> GetExcelData(int id)
        {
            try
            {
                var document = await _databaseService.GetDocumentByIdAsync(id);
                if (document == null)
                {
                    return NotFound("Document not found");
                }

                if (string.IsNullOrEmpty(document.ExcelPath))
                {
                    return BadRequest("This document does not have an Excel file");
                }

                var dataTable = await _documentService.ExtractDataFromExcelAsync(document.ExcelPath);
                
                // Convert DataTable to JSON
                var result = new
                {
                    columns = dataTable.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray(),
                    rows = dataTable.Rows.Cast<DataRow>().Select(row => 
                        dataTable.Columns.Cast<DataColumn>().Select(col => row[col]).ToArray()
                    ).ToArray()
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("excel/{id}/info")]
        public async Task<IActionResult> GetExcelInfo(int id)
        {
            try
            {
                var document = await _databaseService.GetDocumentByIdAsync(id);
                if (document == null)
                {
                    return NotFound("Document not found");
                }

                if (string.IsNullOrEmpty(document.ExcelPath))
                {
                    return BadRequest("This document does not have an Excel file");
                }

                var info = await _documentService.GetExcelFileInfoAsync(document.ExcelPath);
                return Ok(new { info });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
} 