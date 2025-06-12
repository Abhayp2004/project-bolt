using System;

namespace PDFCommenter.Models
{
    public class Document
    {
        public int Id { get; set; }
        public string PDFName { get; set; }
        public string PDFPath { get; set; }
        public int? PDFPages { get; set; }
        public string UploadedBy { get; set; }
        public float FileSizeMB { get; set; }
        public string Status { get; set; }
        public string ExcelName { get; set; }
        public string ExcelPath { get; set; }
        public float? ExcelSizeMB { get; set; }
    }
}