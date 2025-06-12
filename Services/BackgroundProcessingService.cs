using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using PDFCommenter.Data;

namespace PDFCommenter.Services
{
    public class BackgroundProcessingService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<BackgroundProcessingService> _logger;
        private readonly int _processingIntervalMinutes;

        public BackgroundProcessingService(
            IServiceProvider services,
            ILogger<BackgroundProcessingService> logger,
            IConfiguration configuration)
        {
            _services = services;
            _logger = logger;
            _processingIntervalMinutes = configuration.GetValue<int>("FileSettings:ProcessingIntervalMinutes", 30);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Background Processing Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Background Processing Service is running...");
                
                try
                {
                    await ProcessPendingDocumentsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while processing documents.");
                }

                // Wait for the next processing interval
                await Task.Delay(TimeSpan.FromMinutes(_processingIntervalMinutes), stoppingToken);
            }

            _logger.LogInformation("Background Processing Service is stopping.");
        }

        private async Task ProcessPendingDocumentsAsync()
        {
            using var scope = _services.CreateScope();
            var databaseService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
            var processingService = scope.ServiceProvider.GetRequiredService<ProcessingService>();

            var pendingDocuments = await databaseService.GetPendingDocumentsAsync();
            _logger.LogInformation($"Found {pendingDocuments.Count} pending documents to process.");

            foreach (var document in pendingDocuments)
            {
                try
                {
                    _logger.LogInformation($"Processing document: {document.PDFName} (ID: {document.Id})");
                    await processingService.ProcessDocumentAsync(document);
                    _logger.LogInformation($"Successfully processed document: {document.PDFName} (ID: {document.Id})");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing document: {document.PDFName} (ID: {document.Id})");
                }
            }
        }
    }
}