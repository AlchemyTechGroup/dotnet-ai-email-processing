using AiGmailLabeler.Core.Configuration;
using AiGmailLabeler.Core.Services;
using AiGmailLabeler.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace AiGmailLabeler.Console;

public class EmailProcessingWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AppConfiguration _config;
    private readonly ILogger<EmailProcessingWorker> _logger;
    private GmailAuth? _gmailAuth;
    private GmailClient? _gmailClient;

    public EmailProcessingWorker(
        IServiceProvider serviceProvider,
        AppConfiguration config,
        ILogger<EmailProcessingWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _config = config;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AI Gmail Labeler starting up...");
        _logger.LogInformation("Configuration: Poll interval={PollInterval}s, Max results={MaxResults}, Model={Model}",
            _config.PollIntervalSeconds, _config.MaxResults, _config.OllamaModel);

        try
        {
            await InitializeServicesAsync(cancellationToken);
            _logger.LogInformation("Initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize services");
            throw;
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting email processing loop");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessEmailsAsync(stoppingToken);

                var delay = TimeSpan.FromSeconds(_config.PollIntervalSeconds);
                _logger.LogDebug("Waiting {DelaySeconds} seconds until next poll", delay.TotalSeconds);

                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Email processing cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in email processing loop");

                // Wait before retrying to avoid tight error loops
                var backoffDelay = TimeSpan.FromSeconds(Math.Min(_config.BackoffMinSeconds * 2, _config.BackoffMaxSeconds));
                _logger.LogInformation("Waiting {BackoffSeconds} seconds before retrying due to error", backoffDelay.TotalSeconds);

                try
                {
                    await Task.Delay(backoffDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Email processing loop stopped");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AI Gmail Labeler shutting down...");
        await base.StopAsync(cancellationToken);
        _logger.LogInformation("Shutdown completed");
    }

    private async Task InitializeServicesAsync(CancellationToken cancellationToken)
    {
        // Initialize Gmail authentication
        _gmailAuth = _serviceProvider.GetRequiredService<GmailAuth>();

        // Check if we're already authenticated
        var isAuthenticated = await _gmailAuth.IsAuthenticatedAsync(cancellationToken);
        if (!isAuthenticated)
        {
            _logger.LogInformation("Gmail authentication required. Opening browser for OAuth flow...");
        }

        // Get Gmail service (this will trigger OAuth if needed)
        var gmailService = await _gmailAuth.GetGmailServiceAsync(cancellationToken);

        // Create Gmail client with the authenticated service
        var textExtractor = _serviceProvider.GetRequiredService<MimeTextExtractor>();
        _gmailClient = new GmailClient(gmailService, _config, textExtractor, _serviceProvider.GetRequiredService<ILogger<GmailClient>>());

        // Ensure required labels exist
        await _gmailClient.EnsureLabelExistsAsync(_config.AiPhishingLabel, cancellationToken);
        await _gmailClient.EnsureLabelExistsAsync(_config.AiProcessedLabel, cancellationToken);

        _logger.LogInformation("Gmail authentication and label setup completed");
    }

    private async Task ProcessEmailsAsync(CancellationToken cancellationToken)
    {
        if (_gmailClient == null)
        {
            throw new InvalidOperationException("Gmail client not initialized");
        }

        // Get recent messages
        var messageIds = await _gmailClient.ListRecentMessagesAsync(cancellationToken);

        if (messageIds.Count == 0)
        {
            _logger.LogDebug("No new messages found");
            return;
        }

        _logger.LogInformation("Processing {MessageCount} messages", messageIds.Count);

        var processedCount = 0;
        var errorCount = 0;

        foreach (var messageId in messageIds)
        {
            try
            {
                await ProcessSingleMessageAsync(messageId, cancellationToken);
                processedCount++;

                // Small delay between messages to be respectful to the API
                await Task.Delay(100, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errorCount++;
                _logger.LogError(ex, "Failed to process message {MessageId}", messageId);

                // Continue with other messages even if one fails
            }
        }

        _logger.LogInformation("Completed processing batch: {ProcessedCount} processed, {ErrorCount} errors",
            processedCount, errorCount);
    }

    private async Task ProcessSingleMessageAsync(string messageId, CancellationToken cancellationToken)
    {
        if (_gmailClient == null)
        {
            throw new InvalidOperationException("Gmail client not initialized");
        }

        _logger.LogDebug("Processing message {MessageId}", messageId);

        // Get full message context
        var configuration = _serviceProvider.GetRequiredService<IConfiguration>();
        var logger = _serviceProvider.GetRequiredService<ILogger<PipelineCoordinator>>();
        var messageContext = await _gmailClient.GetFullMessageAsync(messageId, configuration, logger, cancellationToken);

        // Log message details (privacy-aware)
        if (_config.VerboseMode)
        {
            _logger.LogInformation("Processing message - ID: {MessageId}, Subject: '{Subject}', From: '{From}', Body length: {BodyLength}",
                messageContext.MessageId, messageContext.Headers.Subject, messageContext.Headers.From, messageContext.BodyText.Length);
        }
        else
        {
            _logger.LogInformation("Processing message - ID: {MessageId}, Subject length: {SubjectLength}, Body length: {BodyLength}",
                messageContext.MessageId, messageContext.Headers.Subject?.Length ?? 0, messageContext.BodyText.Length);
        }

        // Process through the pipeline
        var pipelineCoordinator = _serviceProvider.GetRequiredService<PipelineCoordinator>();
        var result = await pipelineCoordinator.ProcessMessageAsync(messageContext, _gmailClient, cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation("Successfully processed message {MessageId} - Processors: [{Processors}], Added labels: [{AddedLabels}], Removed labels: [{RemovedLabels}]",
                messageId,
                string.Join(", ", result.ProcessorsExecuted),
                string.Join(", ", result.AddedLabels),
                string.Join(", ", result.RemovedLabels));
        }
        else
        {
            _logger.LogWarning("Failed to process message {MessageId}: {ErrorMessage}", messageId, result.ErrorMessage);
        }

        // Log processing notes if any
        if (result.Notes.Any())
        {
            foreach (var note in result.Notes)
            {
                _logger.LogDebug("Processing note for message {MessageId}: {Note}", messageId, note);
            }
        }
    }
}
