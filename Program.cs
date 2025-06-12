using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PDFCommenter.Services;
using PDFCommenter.Data;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages().AddRazorRuntimeCompilation();
builder.Services.AddSingleton<PDFCommenter.Data.DatabaseService>();
builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<ProcessingService>();
builder.Services.AddHostedService<BackgroundProcessingService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Ensure directories exist with proper permissions
var uploadsDir = Path.Combine(app.Environment.WebRootPath, "uploads", "pdfs");
Directory.CreateDirectory(uploadsDir);
// Ensure the directory is writable
var dirInfo = new DirectoryInfo(uploadsDir);
if (!dirInfo.Exists)
{
    throw new DirectoryNotFoundException($"Could not create uploads directory at {uploadsDir}");
}

// Configure static files
app.UseStaticFiles();

// Custom middleware to handle PDF files with proper MIME type
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    if (path != null && path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.ContentType = "application/pdf";
        context.Response.Headers.Add("Content-Disposition", "inline");
    }
    await next();
});

app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();

app.Run();