using AiGmailLabeler.Core.Configuration;
using AiGmailLabeler.Core.Interfaces;
using AiGmailLabeler.Core.Models;
using Microsoft.Extensions.Logging;

namespace AiGmailLabeler.Core.Processors;

public class TestKeywordProcessor : IEmailProcessor
{
    private readonly AppConfiguration _config;
    private readonly ILogger<TestKeywordProcessor> _logger;

    // Test keywords that will trigger labeling
    private readonly string[] _testKeywords = ["TEST", "DEMO", "URGENT", "WINNER", "CONGRATULATIONS"];

    public TestKeywordProcessor(AppConfiguration config, ILogger<TestKeywordProcessor> logger)
    {
        _config = config;
        _logger = logger;
    }

    public Task<ProcessorOutcome> ProcessAsync(MessageContext context, CancellationToken cancellationToken = default)
    {
        if (!_config.Processors.TestKeyword.Enabled)
        {
            return Task.FromResult(ProcessorOutcome.Skipped("TestKeyword processor is disabled"));
        }

        try
        {
            var subject = context.Headers.Subject ?? "";
            var body = context.BodyText ?? "";
            var combinedText = $"{subject} {body}".ToUpperInvariant();

            var foundKeywords = _testKeywords.Where(keyword =>
                combinedText.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();

            if (foundKeywords.Any())
            {
                var testLabel = "[TEST]: Keyword Match";
                var reason = $"Found test keywords: {string.Join(", ", foundKeywords)}";

                _logger.LogInformation("TestKeyword processor found keywords in message {MessageId}: {Keywords}",
                    context.MessageId, string.Join(", ", foundKeywords));

                return Task.FromResult(ProcessorOutcome.Handled([testLabel], reason));
            }
            else
            {
                _logger.LogDebug("TestKeyword processor found no matching keywords in message {MessageId}",
                    context.MessageId);

                return Task.FromResult(ProcessorOutcome.Handled("No test keywords found"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestKeyword processor for message {MessageId}", context.MessageId);
            return Task.FromResult(ProcessorOutcome.Failed($"TestKeyword processor error: {ex.Message}"));
        }
    }
}
