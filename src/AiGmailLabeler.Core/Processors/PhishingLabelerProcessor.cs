using AiGmailLabeler.Core.Configuration;
using AiGmailLabeler.Core.Interfaces;
using AiGmailLabeler.Core.Models;
using AiGmailLabeler.Core.Services;
using Microsoft.Extensions.Logging;

namespace AiGmailLabeler.Core.Processors;

public class PhishingLabelerProcessor : IEmailProcessor
{
    private readonly AppConfiguration _config;
    private readonly ILogger<PhishingLabelerProcessor> _logger;

    public PhishingLabelerProcessor(
        AppConfiguration config,
        ILogger<PhishingLabelerProcessor> logger)
    {
        _config = config;
        _logger = logger;
    }

    public Task<ProcessorOutcome> ProcessAsync(MessageContext context, CancellationToken cancellationToken = default)
    {
        if (!_config.Processors.PhishingLabeler.Enabled)
        {
            return Task.FromResult(ProcessorOutcome.Skipped("PhishingLabeler processor is disabled"));
        }

        try
        {
            // Get the classification result from the previous processor
            var classificationResult = context.GetClassificationResult();

            if (classificationResult == null)
            {
                _logger.LogWarning("No classification result found for message {MessageId}, cannot apply phishing label",
                    context.MessageId);
                return Task.FromResult(ProcessorOutcome.Skipped("No classification result available"));
            }

            var confidenceThreshold = GetConfidenceThreshold();

            // Check if the message should be labeled as phishing
            if (classificationResult.IsPhishing && classificationResult.Confidence >= confidenceThreshold)
            {
                _logger.LogInformation("Message {MessageId} meets phishing criteria (confidence: {Confidence:F3} >= {Threshold:F3}), applying phishing label",
                    context.MessageId, classificationResult.Confidence, confidenceThreshold);

                var outcome = ProcessorOutcome.Handled(
                    [_config.AiPhishingLabel],
                    $"Applied phishing label based on classification (confidence: {classificationResult.Confidence:F3})");

                // Clean up the stored classification result
                context.ClearClassificationResult();

                return Task.FromResult(outcome);
            }
            else if (classificationResult.IsPhishing)
            {
                _logger.LogInformation("Message {MessageId} classified as phishing but confidence {Confidence:F3} below threshold {Threshold:F3}, not applying label",
                    context.MessageId, classificationResult.Confidence, confidenceThreshold);

                var outcome = ProcessorOutcome.Handled($"Phishing detected but confidence {classificationResult.Confidence:F3} below threshold {confidenceThreshold:F3}");

                // Clean up the stored classification result
                context.ClearClassificationResult();

                return Task.FromResult(outcome);
            }
            else
            {
                _logger.LogDebug("Message {MessageId} classified as benign (confidence: {Confidence:F3}), no phishing label applied",
                    context.MessageId, classificationResult.Confidence);

                var outcome = ProcessorOutcome.Handled($"Message classified as benign (confidence: {classificationResult.Confidence:F3})");

                // Clean up the stored classification result
                context.ClearClassificationResult();

                return Task.FromResult(outcome);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying phishing label to message {MessageId}", context.MessageId);

            // Clean up on error
            try
            {
                context.ClearClassificationResult();
            }
            catch
            {
                // Ignore cleanup errors
            }

            return Task.FromResult(ProcessorOutcome.Failed($"Labeling error: {ex.Message}"));
        }
    }

    private double GetConfidenceThreshold()
    {
        // Use processor-specific confidence threshold if set, otherwise use global threshold
        return _config.Processors.PhishingClassifier.Confidence ?? _config.ClassifyConfidenceThreshold;
    }
}
