using AiGmailLabeler.Core.Configuration;
using AiGmailLabeler.Core.Interfaces;
using AiGmailLabeler.Core.Models;
using AiGmailLabeler.Core.Services;
using Microsoft.Extensions.Logging;

namespace AiGmailLabeler.Core.Processors;

public class NoiseFilterProcessor : IEmailProcessor
{
    private readonly AppConfiguration _config;
    private readonly MimeTextExtractor _textExtractor;
    private readonly ILogger<NoiseFilterProcessor> _logger;

    public NoiseFilterProcessor(
        AppConfiguration config,
        MimeTextExtractor textExtractor,
        ILogger<NoiseFilterProcessor> logger)
    {
        _config = config;
        _textExtractor = textExtractor;
        _logger = logger;
    }

    public Task<ProcessorOutcome> ProcessAsync(MessageContext context, CancellationToken cancellationToken = default)
    {
        if (!_config.Processors.NoiseFilter.Enabled)
        {
            return Task.FromResult(ProcessorOutcome.Skipped("NoiseFilter processor is disabled"));
        }

        try
        {
            // Check if the message has usable text content
            if (!_textExtractor.HasUsableText(context.BodyText))
            {
                _logger.LogDebug("Message {MessageId} has insufficient text content for analysis (length: {Length})",
                    context.MessageId, context.BodyText.Length);

                return Task.FromResult(ProcessorOutcome.StopChain("Message lacks sufficient text content for analysis"));
            }

            // Check for common patterns that indicate non-human messages
            if (IsAutomatedMessage(context))
            {
                _logger.LogDebug("Message {MessageId} appears to be automated, skipping analysis", context.MessageId);
                return Task.FromResult(ProcessorOutcome.StopChain("Message appears to be automated"));
            }

            // Check if it's a newsletter or bulk email (less likely to be phishing)
            if (IsBulkEmail(context))
            {
                _logger.LogDebug("Message {MessageId} appears to be bulk/newsletter email", context.MessageId);
                return Task.FromResult(ProcessorOutcome.Handled("Message is bulk email, proceeding with caution"));
            }

            _logger.LogDebug("Message {MessageId} passed noise filtering, has {TextLength} characters of usable text",
                context.MessageId, context.BodyText.Length);

            return Task.FromResult(ProcessorOutcome.Handled("Message has sufficient content for analysis"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in noise filtering for message {MessageId}", context.MessageId);
            return Task.FromResult(ProcessorOutcome.Failed($"Noise filter error: {ex.Message}"));
        }
    }

    private bool IsAutomatedMessage(MessageContext context)
    {
        var subject = context.Headers.Subject?.ToLowerInvariant() ?? string.Empty;
        var from = context.Headers.From?.ToLowerInvariant() ?? string.Empty;
        var bodyText = context.BodyText.ToLowerInvariant();

        // Check for automated message indicators
        var automatedPatterns = new[]
        {
            "noreply", "no-reply", "donotreply", "do-not-reply",
            "automatic", "automated", "system generated",
            "delivery status notification", "mail delivery subsystem",
            "postmaster", "mailer-daemon"
        };

        var subjectAutomatedPatterns = new[]
        {
            "out of office", "auto-reply", "automatic reply", "vacation response",
            "delivery failure", "undelivered mail", "mail delivery failed",
            "receipt", "confirmation", "notification"
        };

        // Check sender patterns
        if (automatedPatterns.Any(pattern => from.Contains(pattern)))
        {
            return true;
        }

        // Check subject patterns
        if (subjectAutomatedPatterns.Any(pattern => subject.Contains(pattern)))
        {
            return true;
        }

        // Check for common automated message content
        var bodyAutomatedPatterns = new[]
        {
            "this is an automated message", "automatically generated",
            "please do not reply", "unsubscribe", "manage your preferences",
            "delivery has failed", "message could not be delivered"
        };

        if (bodyAutomatedPatterns.Any(pattern => bodyText.Contains(pattern)))
        {
            return true;
        }

        return false;
    }

    private bool IsBulkEmail(MessageContext context)
    {
        var from = context.Headers.From?.ToLowerInvariant() ?? string.Empty;
        var bodyText = context.BodyText.ToLowerInvariant();

        // Check for common bulk email indicators
        var bulkPatterns = new[]
        {
            "newsletter", "mailing list", "bulk mail", "marketing",
            "unsubscribe", "manage preferences", "view this email in your browser",
            "if you no longer wish to receive", "update your email preferences"
        };

        // Check for common sender patterns
        var bulkSenderPatterns = new[]
        {
            "newsletter", "marketing", "noreply", "no-reply",
            "info@", "news@", "updates@", "alerts@"
        };

        if (bulkSenderPatterns.Any(pattern => from.Contains(pattern)))
        {
            return true;
        }

        if (bulkPatterns.Any(pattern => bodyText.Contains(pattern)))
        {
            return true;
        }

        return false;
    }
}
