using AiGmailLabeler.Core.Configuration;
using AiGmailLabeler.Core.Interfaces;
using AiGmailLabeler.Core.Models;
using AiGmailLabeler.Core.Services;
using Microsoft.Extensions.Logging;

namespace AiGmailLabeler.Core.Processors;

public class PhishingClassifierProcessor : IEmailProcessor
{
    private readonly AppConfiguration _config;
    private readonly OllamaClient _ollamaClient;
    private readonly ILogger<PhishingClassifierProcessor> _logger;

    public PhishingClassifierProcessor(
        AppConfiguration config,
        OllamaClient ollamaClient,
        ILogger<PhishingClassifierProcessor> logger)
    {
        _config = config;
        _ollamaClient = ollamaClient;
        _logger = logger;
    }

    public async Task<ProcessorOutcome> ProcessAsync(MessageContext context, CancellationToken cancellationToken = default)
    {
        if (!_config.Processors.PhishingClassifier.Enabled)
        {
            return ProcessorOutcome.Skipped("PhishingClassifier processor is disabled");
        }

        try
        {
            // Prepare the message text for classification
            var messageText = PrepareMessageForClassification(context);

            if (string.IsNullOrWhiteSpace(messageText))
            {
                _logger.LogWarning("Message {MessageId} has no content for classification", context.MessageId);
                return ProcessorOutcome.Skipped("Message has no content for classification");
            }

            _logger.LogDebug("Classifying message {MessageId} with {TextLength} characters",
                context.MessageId, messageText.Length);

            // Classify the message using Ollama
            var classificationResult = await _ollamaClient.ClassifyMessageAsync(messageText, cancellationToken);

            if (classificationResult.IsError)
            {
                _logger.LogError("Classification failed for message {MessageId}: {Error}",
                    context.MessageId, classificationResult.ErrorMessage);
                return ProcessorOutcome.Failed($"Classification error: {classificationResult.ErrorMessage}");
            }

            // Log the classification result
            var confidenceThreshold = GetConfidenceThreshold();
            _logger.LogInformation("Message {MessageId} classified: phishing={IsPhishing}, confidence={Confidence:F3}, threshold={Threshold:F3}, reason='{Reason}'",
                context.MessageId,
                classificationResult.IsPhishing,
                classificationResult.Confidence,
                confidenceThreshold,
                classificationResult.Reason);

            // Store classification result for use by other processors
            context.SetClassificationResult(classificationResult);

            // Create a note with the classification details
            var classificationNote = $"Classified as {(classificationResult.IsPhishing ? "phishing" : "benign")} " +
                                   $"with {classificationResult.Confidence:F3} confidence: {classificationResult.Reason}";

            return ProcessorOutcome.Handled(classificationNote);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error classifying message {MessageId}", context.MessageId);
            return ProcessorOutcome.Failed($"Classification error: {ex.Message}");
        }
    }

    private string PrepareMessageForClassification(MessageContext context)
    {
        var text = new System.Text.StringBuilder();

        // Include subject if available
        if (!string.IsNullOrEmpty(context.Headers.Subject))
        {
            text.AppendLine($"Subject: {context.Headers.Subject}");
        }

        // Include sender if available
        if (!string.IsNullOrEmpty(context.Headers.From))
        {
            text.AppendLine($"From: {context.Headers.From}");
        }

        // Add separator
        text.AppendLine("---");

        // Add body text
        text.Append(context.BodyText);

        return text.ToString();
    }

    private double GetConfidenceThreshold()
    {
        // Use processor-specific confidence threshold if set, otherwise use global threshold
        return _config.Processors.PhishingClassifier.Confidence ?? _config.ClassifyConfidenceThreshold;
    }

}

// Extension to MessageContext to support classification results
public static class MessageContextExtensions
{
    private static readonly Dictionary<string, ClassificationResult> _classificationResults = new();

    public static void SetClassificationResult(this MessageContext context, ClassificationResult result)
    {
        _classificationResults[context.MessageId] = result;
    }

    public static ClassificationResult? GetClassificationResult(this MessageContext context)
    {
        return _classificationResults.TryGetValue(context.MessageId, out var result) ? result : null;
    }

    public static void ClearClassificationResult(this MessageContext context)
    {
        _classificationResults.Remove(context.MessageId);
    }
}
