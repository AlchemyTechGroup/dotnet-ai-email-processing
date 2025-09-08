using Google.Apis.Gmail.v1.Data;
using AiGmailLabeler.Core.Configuration;
using AiGmailLabeler.Core.Interfaces;
using AiGmailLabeler.Core.Models;
using Microsoft.Extensions.Logging;

namespace AiGmailLabeler.Core.Processors;

public class DeduplicateProcessor : IEmailProcessor
{
    private readonly AppConfiguration _config;
    private readonly ILogger<DeduplicateProcessor> _logger;

    public DeduplicateProcessor(AppConfiguration config, ILogger<DeduplicateProcessor> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<ProcessorOutcome> ProcessAsync(MessageContext context, CancellationToken cancellationToken = default)
    {
        if (!_config.Processors.Deduplicate.Enabled)
        {
            return ProcessorOutcome.Skipped("Deduplicate processor is disabled");
        }

        try
        {
            // Get the current message to check its labels
            var message = await context.GmailService.Users.Messages.Get("me", context.MessageId).ExecuteAsync(cancellationToken);

            var labelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (message.LabelIds != null)
            {
                // Get all labels to resolve IDs to names
                var allLabels = await context.GmailService.Users.Labels.List("me").ExecuteAsync(cancellationToken);
                var labelMap = allLabels.Labels?.ToDictionary(l => l.Id!, l => l.Name!, StringComparer.OrdinalIgnoreCase)
                              ?? new Dictionary<string, string>();

                // Convert label IDs to names
                foreach (var labelId in message.LabelIds)
                {
                    if (labelMap.TryGetValue(labelId, out var labelName))
                    {
                        labelNames.Add(labelName);
                    }
                }
            }

            // Check if the message already has the AI processed label
            if (labelNames.Contains(_config.AiProcessedLabel))
            {
                _logger.LogDebug("Message {MessageId} already has processed label '{ProcessedLabel}', skipping processing",
                    context.MessageId, _config.AiProcessedLabel);

                return ProcessorOutcome.StopChain("Message already processed");
            }

            _logger.LogDebug("Message {MessageId} has not been processed yet, continuing with pipeline", context.MessageId);
            return ProcessorOutcome.Handled("Message is not a duplicate");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for duplicate processing in message {MessageId}", context.MessageId);
            return ProcessorOutcome.Failed($"Error checking duplicates: {ex.Message}");
        }
    }
}
