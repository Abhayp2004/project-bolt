using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace PDFCommenter.Models
{
    public class UploadViewModel
    {
        [Required(ErrorMessage = "Please select a PDF file")]
        public IFormFile PDFFile { get; set; }

        public IFormFile ExcelFile { get; set; }

        [Range(0.5, 3.0, ErrorMessage = "Zoom factor must be between 0.5 and 3.0")]
        public double ZoomFactor { get; set; } = 1.0;
    }
}