using AiGmailLabeler.Core.Configuration;
using AiGmailLabeler.Core.Interfaces;
using AiGmailLabeler.Core.Models;
using AiGmailLabeler.Core.Services;
using AiGmailLabeler.Core.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiGmailLabeler.Core.Services;

public class PipelineCoordinator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AppConfiguration _config;
    private readonly ILogger<PipelineCoordinator> _logger;

    public PipelineCoordinator(
        IServiceProvider serviceProvider,
        AppConfiguration config,
        ILogger<PipelineCoordinator> logger)
    {
        _serviceProvider = serviceProvider;
        _config = config;
        _logger = logger;
    }

    public async Task<PipelineResult> ProcessMessageAsync(
        MessageContext context,
        GmailClient gmailClient,
        CancellationToken cancellationToken = default)
    {
        var processorNames = _config.GetProcessorOrderList();
        var allAddedLabels = new HashSet<string>();
        var allRemovedLabels = new HashSet<string>();
        var processingNotes = new List<string>();
        var processorsExecuted = new List<string>();

        _logger.LogInformation("Starting pipeline processing for message {MessageId} with {ProcessorCount} processors: {Processors}",
            context.MessageId, processorNames.Count, string.Join(", ", processorNames));

        try
        {
            foreach (var processorName in processorNames)
            {
                var processor = ResolveProcessor(processorName);
                if (processor == null)
                {
                    _logger.LogWarning("Processor {ProcessorName} not found, skipping", processorName);
                    continue;
                }

                _logger.LogDebug("Executing processor: {ProcessorName}", processorName);

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                ProcessorOutcome outcome;

                try
                {
                    outcome = await processor.ProcessAsync(context, cancellationToken);
                    stopwatch.Stop();
                    processorsExecuted.Add(processorName);
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    _logger.LogError(ex, "Processor {ProcessorName} threw an exception", processorName);

                    outcome = ProcessorOutcome.Failed($"Exception in {processorName}: {ex.Message}");
                }

                _logger.LogDebug("Processor {ProcessorName} completed in {ElapsedMs}ms with status: {Status}",
                    processorName, stopwatch.ElapsedMilliseconds, outcome.Status);

                // Accumulate label changes
                foreach (var label in outcome.AddedLabels)
                {
                    allAddedLabels.Add(label);
                    // Remove from removed labels if it was there (in case of conflicting operations)
                    allRemovedLabels.Remove(label);
                }

                foreach (var label in outcome.RemovedLabels)
                {
                    allRemovedLabels.Add(label);
                    // Remove from added labels if it was there (in case of conflicting operations)
                    allAddedLabels.Remove(label);
                }

                // Add notes if provided
                if (!string.IsNullOrEmpty(outcome.Notes))
                {
                    processingNotes.Add($"{processorName}: {outcome.Notes}");
                }

                // Handle processor outcomes
                switch (outcome.Status)
                {
                    case ProcessorStatus.Handled:
                        _logger.LogDebug("Processor {ProcessorName} handled the message", processorName);
                        break;

                    case ProcessorStatus.Skipped:
                        _logger.LogDebug("Processor {ProcessorName} skipped the message", processorName);
                        break;

                    case ProcessorStatus.Failed:
                        _logger.LogWarning("Processor {ProcessorName} failed: {Notes}", processorName, outcome.Notes);
                        break;

                    case ProcessorStatus.StopChain:
                        _logger.LogInformation("Processor {ProcessorName} requested to stop the processing chain: {Notes}",
                            processorName, outcome.Notes);
                        goto ProcessingComplete; // Break out of the foreach loop
                }
            }

            ProcessingComplete:

            // Apply accumulated label changes
            await ApplyLabelChangesAsync(context.MessageId, allAddedLabels, allRemovedLabels, gmailClient, cancellationToken);

            // Always add the processed label to prevent reprocessing
            await gmailClient.ApplyLabelChangesAsync(
                context.MessageId,
                [_config.AiProcessedLabel],
                [],
                cancellationToken);

            var result = new PipelineResult
            {
                Success = true,
                ProcessorsExecuted = processorsExecuted,
                AddedLabels = allAddedLabels.ToList(),
                RemovedLabels = allRemovedLabels.ToList(),
                Notes = processingNotes
            };

            _logger.LogInformation("Pipeline processing completed for message {MessageId}: {AddedCount} labels added, {RemovedCount} labels removed",
                context.MessageId, allAddedLabels.Count, allRemovedLabels.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline processing failed for message {MessageId}", context.MessageId);

            return new PipelineResult
            {
                Success = false,
                ProcessorsExecuted = processorsExecuted,
                ErrorMessage = ex.Message,
                Notes = processingNotes
            };
        }
    }

    private IEmailProcessor? ResolveProcessor(string processorName)
    {
        try
        {
            // Map processor names to their service keys
            var processorKey = processorName switch
            {
                "Deduplicate" => typeof(DeduplicateProcessor),
                "NoiseFilter" => typeof(NoiseFilterProcessor),
                "PhishingClassifier" => typeof(PhishingClassifierProcessor),
                "PhishingLabeler" => typeof(PhishingLabelerProcessor),
                "TestKeyword" => typeof(TestKeywordProcessor),
                _ => null
            };

            if (processorKey == null)
            {
                _logger.LogWarning("Unknown processor name: {ProcessorName}", processorName);
                return null;
            }

            var processor = _serviceProvider.GetService(processorKey) as IEmailProcessor;

            if (processor == null)
            {
                _logger.LogWarning("Processor {ProcessorName} is not registered in DI container", processorName);
            }

            return processor;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve processor {ProcessorName}", processorName);
            return null;
        }
    }

    private async Task ApplyLabelChangesAsync(
        string messageId,
        ICollection<string> addedLabels,
        ICollection<string> removedLabels,
        GmailClient gmailClient,
        CancellationToken cancellationToken)
    {
        if (!addedLabels.Any() && !removedLabels.Any())
        {
            _logger.LogDebug("No label changes to apply for message {MessageId}", messageId);
            return;
        }

        try
        {
            await gmailClient.ApplyLabelChangesAsync(messageId, addedLabels, removedLabels, cancellationToken);

            _logger.LogInformation("Applied label changes to message {MessageId}: +[{AddedLabels}] -[{RemovedLabels}]",
                messageId,
                string.Join(", ", addedLabels),
                string.Join(", ", removedLabels));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply label changes to message {MessageId}", messageId);
            throw;
        }
    }
}

public record PipelineResult
{
    public bool Success { get; init; }
    public IReadOnlyList<string> ProcessorsExecuted { get; init; } = [];
    public IReadOnlyList<string> AddedLabels { get; init; } = [];
    public IReadOnlyList<string> RemovedLabels { get; init; } = [];
    public IReadOnlyList<string> Notes { get; init; } = [];
    public string? ErrorMessage { get; init; }
}
