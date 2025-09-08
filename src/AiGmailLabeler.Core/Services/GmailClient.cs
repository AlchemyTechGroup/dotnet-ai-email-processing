using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google;
using AiGmailLabeler.Core.Configuration;
using AiGmailLabeler.Core.Models;
using Microsoft.Extensions.Logging;
using System.Net;

namespace AiGmailLabeler.Core.Services;

public class GmailClient
{
    private readonly GmailService _service;
    private readonly AppConfiguration _config;
    private readonly MimeTextExtractor _textExtractor;
    private readonly ILogger<GmailClient> _logger;
    private readonly Dictionary<string, string> _labelCache = new();

    public GmailClient(GmailService service, AppConfiguration config, MimeTextExtractor textExtractor, ILogger<GmailClient> logger)
    {
        _service = service;
        _config = config;
        _textExtractor = textExtractor;
        _logger = logger;
    }

    public async Task EnsureLabelExistsAsync(string labelName, CancellationToken cancellationToken = default)
    {
        // Check cache first
        if (_labelCache.ContainsKey(labelName))
        {
            return;
        }

        await ExecuteWithBackoffAsync(async () =>
        {
            var existingLabels = await _service.Users.Labels.List("me").ExecuteAsync(cancellationToken);
            var existingLabel = existingLabels.Labels?.FirstOrDefault(l =>
                string.Equals(l.Name, labelName, StringComparison.OrdinalIgnoreCase));

            if (existingLabel != null)
            {
                _labelCache[labelName] = existingLabel.Id!;
                _logger.LogDebug("Found existing label: {LabelName} (ID: {LabelId})", labelName, existingLabel.Id);
                return;
            }

            // Create the label
            var newLabel = new Label
            {
                Name = labelName,
                LabelListVisibility = "labelShow",
                MessageListVisibility = "show"
            };

            var createdLabel = await _service.Users.Labels.Create(newLabel, "me").ExecuteAsync(cancellationToken);
            _labelCache[labelName] = createdLabel.Id!;
            _logger.LogInformation("Created new Gmail label: {LabelName} (ID: {LabelId})", labelName, createdLabel.Id);

        }, $"ensure label exists: {labelName}", cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListRecentMessagesAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithBackoffAsync(async () =>
        {
            var request = _service.Users.Messages.List("me");
            request.Q = _config.GmailQuery;
            request.MaxResults = _config.MaxResults;

            var response = await request.ExecuteAsync(cancellationToken);
            var messageIds = response.Messages?.Select(m => m.Id!).ToList() ?? [];

            _logger.LogInformation("Found {MessageCount} recent messages matching query: {Query}",
                messageIds.Count, _config.GmailQuery);

            return (IReadOnlyList<string>)messageIds;

        }, "list recent messages", cancellationToken);
    }

    public async Task<MessageContext> GetFullMessageAsync(
        string messageId,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithBackoffAsync(async () =>
        {
            var message = await _service.Users.Messages.Get("me", messageId).ExecuteAsync(cancellationToken);

            var headers = ExtractHeaders(message);
            var bodyText = _textExtractor.ExtractText(message, _config.BodyMaxKb);

            var context = new MessageContext
            {
                GmailService = _service,
                MessageId = messageId,
                ThreadId = message.ThreadId ?? string.Empty,
                Headers = headers,
                BodyText = bodyText,
                Configuration = configuration,
                Logger = logger,
                CancellationToken = cancellationToken
            };

            _logger.LogDebug("Retrieved full message: {MessageId}, Subject: {Subject}, Body length: {BodyLength}",
                messageId, headers.Subject, bodyText.Length);

            return context;

        }, $"get full message: {messageId}", cancellationToken);
    }

    public async Task ApplyLabelChangesAsync(
        string messageId,
        IEnumerable<string> labelsToAdd,
        IEnumerable<string> labelsToRemove,
        CancellationToken cancellationToken = default)
    {
        var addLabels = labelsToAdd.ToList();
        var removeLabels = labelsToRemove.ToList();

        if (!addLabels.Any() && !removeLabels.Any())
        {
            return;
        }

        // Ensure all labels exist and get their IDs
        var addLabelIds = new List<string>();
        foreach (var labelName in addLabels)
        {
            await EnsureLabelExistsAsync(labelName, cancellationToken);
            addLabelIds.Add(_labelCache[labelName]);
        }

        var removeLabelIds = new List<string>();
        foreach (var labelName in removeLabels)
        {
            if (_labelCache.TryGetValue(labelName, out var labelId))
            {
                removeLabelIds.Add(labelId);
            }
        }

        await ExecuteWithBackoffAsync(async () =>
        {
            var modifyRequest = new ModifyMessageRequest
            {
                AddLabelIds = addLabelIds.Any() ? addLabelIds : null,
                RemoveLabelIds = removeLabelIds.Any() ? removeLabelIds : null
            };

            await _service.Users.Messages.Modify(modifyRequest, "me", messageId).ExecuteAsync(cancellationToken);

            _logger.LogInformation("Applied label changes to message {MessageId}: +{AddedLabels} -{RemovedLabels}",
                messageId,
                string.Join(",", addLabels),
                string.Join(",", removeLabels));

        }, $"apply label changes to message: {messageId}", cancellationToken);
    }

    private MessageHeaders ExtractHeaders(Message message)
    {
        var headers = message.Payload?.Headers ?? [];

        return new MessageHeaders
        {
            Subject = GetHeaderValue(headers, "Subject"),
            From = GetHeaderValue(headers, "From"),
            To = GetHeaderValue(headers, "To"),
            Date = ParseDate(GetHeaderValue(headers, "Date"))
        };
    }

    private static string? GetHeaderValue(IList<MessagePartHeader> headers, string name)
    {
        return headers.FirstOrDefault(h =>
            string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static DateTime? ParseDate(string? dateString)
    {
        if (string.IsNullOrEmpty(dateString))
            return null;

        return DateTime.TryParse(dateString, out var date) ? date : null;
    }


    private async Task<T> ExecuteWithBackoffAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        var maxAttempts = 5;
        var baseDelay = TimeSpan.FromSeconds(_config.BackoffMinSeconds);
        var maxDelay = TimeSpan.FromSeconds(_config.BackoffMaxSeconds);

        while (attempt < maxAttempts)
        {
            try
            {
                return await operation();
            }
            catch (GoogleApiException ex) when (IsRetryableError(ex) && attempt < maxAttempts - 1)
            {
                attempt++;
                var delay = CalculateBackoffDelay(attempt, baseDelay, maxDelay, ex);

                _logger.LogWarning("Gmail API error on {OperationName} (attempt {Attempt}/{MaxAttempts}): {Error}. Retrying in {Delay}ms",
                    operationName, attempt, maxAttempts, ex.Message, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to {OperationName} after {Attempts} attempts", operationName, attempt + 1);
                throw;
            }
        }

        throw new InvalidOperationException($"Failed to {operationName} after {maxAttempts} attempts");
    }

    private async Task ExecuteWithBackoffAsync(
        Func<Task> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        await ExecuteWithBackoffAsync(async () =>
        {
            await operation();
            return true; // Dummy return value
        }, operationName, cancellationToken);
    }

    private static bool IsRetryableError(GoogleApiException ex)
    {
        return ex.HttpStatusCode == HttpStatusCode.TooManyRequests ||
               ex.HttpStatusCode == HttpStatusCode.InternalServerError ||
               ex.HttpStatusCode == HttpStatusCode.BadGateway ||
               ex.HttpStatusCode == HttpStatusCode.ServiceUnavailable ||
               ex.HttpStatusCode == HttpStatusCode.GatewayTimeout;
    }

    private TimeSpan CalculateBackoffDelay(int attempt, TimeSpan baseDelay, TimeSpan maxDelay, GoogleApiException ex)
    {
        // Check for Retry-After header
        if (ex.HttpStatusCode == HttpStatusCode.TooManyRequests &&
            ex.Message.Contains("Retry-After") &&
            int.TryParse("60", out var retryAfterSeconds)) // Default to 60 seconds for rate limiting
        {
            var retryAfterDelay = TimeSpan.FromSeconds(retryAfterSeconds);
            return retryAfterDelay > maxDelay ? maxDelay : retryAfterDelay;
        }

        // Exponential backoff with jitter
        var exponentialDelay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * 1000);
        var totalDelay = exponentialDelay + jitter;

        return totalDelay > maxDelay ? maxDelay : totalDelay;
    }
}
