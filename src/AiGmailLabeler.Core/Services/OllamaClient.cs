using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiGmailLabeler.Core.Configuration;
using AiGmailLabeler.Core.Models;
using Microsoft.Extensions.Logging;

namespace AiGmailLabeler.Core.Services;

public class OllamaClient
{
    private readonly HttpClient _httpClient;
    private readonly AppConfiguration _config;
    private readonly ILogger<OllamaClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public OllamaClient(HttpClient httpClient, AppConfiguration config, ILogger<OllamaClient> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;

        // Configure HTTP client
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.HttpTimeoutSeconds);

        // Configure JSON serialization options
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<ClassificationResult> ClassifyMessageAsync(
        string messageText,
        CancellationToken cancellationToken = default)
    {
        var prompt = BuildClassificationPrompt(messageText);

        return await ExecuteWithRetryAsync(async () =>
        {
            var request = new OllamaGenerateRequest
            {
                Model = _config.OllamaModel,
                Prompt = prompt,
                Temperature = 0.0f,
                Stream = false,
                Format = "json"
            };

            _logger.LogDebug("Sending classification request to Ollama: model={Model}, prompt_length={PromptLength}",
                _config.OllamaModel, prompt.Length);

            var response = await _httpClient.PostAsJsonAsync(
                $"{_config.OllamaBaseUrl.TrimEnd('/')}/api/generate",
                request,
                _jsonOptions,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var ollamaResponse = JsonSerializer.Deserialize<OllamaGenerateResponse>(responseContent, _jsonOptions);

            if (ollamaResponse?.Response == null)
            {
                throw new InvalidOperationException("Ollama returned empty response");
            }

            _logger.LogDebug("Received classification response from Ollama: length={ResponseLength}",
                ollamaResponse.Response.Length);

            return ParseClassificationResult(ollamaResponse.Response);

        }, "classify message", cancellationToken);
    }

    private string BuildClassificationPrompt(string messageText)
    {
        var prompt = $$"""
You are an email security analyzer. Analyze the following email content and determine if it appears to be a phishing attempt.

Consider these phishing indicators:
- Urgent language or threats
- Requests for personal information, passwords, or financial details
- Suspicious sender or mismatched domains
- Generic greetings instead of personal names
- Poor grammar or spelling
- Suspicious links or attachments mentioned
- Impersonation of legitimate companies or services
- Creating false sense of urgency or fear

Email content to analyze:
{{messageText}}

Respond with valid JSON in this exact format:
{
  "label": "phishing_possible" or "benign",
  "confidence": 0.85,
  "reason": "Brief explanation of the classification decision"
}

The confidence should be a number between 0.0 and 1.0. Only classify as "phishing_possible" if you have reasonable confidence it's suspicious.
""";

        return prompt;
    }

    private ClassificationResult ParseClassificationResult(string responseText)
    {
        try
        {
            // Try to extract JSON from the response (sometimes there's extra text)
            var jsonMatch = System.Text.RegularExpressions.Regex.Match(
                responseText,
                @"\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            if (!jsonMatch.Success)
            {
                _logger.LogWarning("Could not find JSON in Ollama response: {Response}", responseText);
                return ClassificationResult.Error("No JSON found in response");
            }

            var jsonText = jsonMatch.Value;
            var classification = JsonSerializer.Deserialize<ClassificationResponse>(jsonText, _jsonOptions);

            if (classification == null)
            {
                return ClassificationResult.Error("Failed to deserialize classification response");
            }

            // Validate the response
            if (string.IsNullOrEmpty(classification.Label))
            {
                return ClassificationResult.Error("Classification label is missing");
            }

            if (classification.Confidence < 0.0 || classification.Confidence > 1.0)
            {
                _logger.LogWarning("Invalid confidence value: {Confidence}, clamping to valid range", classification.Confidence);
                classification.Confidence = Math.Clamp(classification.Confidence, 0.0, 1.0);
            }

            var isPhishing = string.Equals(classification.Label, "phishing_possible", StringComparison.OrdinalIgnoreCase);

            _logger.LogDebug("Parsed classification: label={Label}, confidence={Confidence}, reason={Reason}",
                classification.Label, classification.Confidence, classification.Reason);

            return new ClassificationResult
            {
                IsPhishing = isPhishing,
                Confidence = classification.Confidence,
                Reason = classification.Reason ?? "No reason provided",
                RawResponse = responseText
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON classification response: {Response}", responseText);
            return ClassificationResult.Error($"JSON parsing error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error parsing classification response");
            return ClassificationResult.Error($"Parsing error: {ex.Message}");
        }
    }

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        var maxAttempts = 3;
        var baseDelay = TimeSpan.FromSeconds(_config.BackoffMinSeconds);
        var maxDelay = TimeSpan.FromSeconds(_config.BackoffMaxSeconds);

        while (attempt < maxAttempts)
        {
            try
            {
                return await operation();
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts - 1)
            {
                attempt++;
                var delay = CalculateBackoffDelay(attempt, baseDelay, maxDelay);

                _logger.LogWarning("HTTP error on {OperationName} (attempt {Attempt}/{MaxAttempts}): {Error}. Retrying in {Delay}ms",
                    operationName, attempt, maxAttempts, ex.Message, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException && attempt < maxAttempts - 1)
            {
                attempt++;
                var delay = CalculateBackoffDelay(attempt, baseDelay, maxDelay);

                _logger.LogWarning("Timeout on {OperationName} (attempt {Attempt}/{MaxAttempts}). Retrying in {Delay}ms",
                    operationName, attempt, maxAttempts, delay.TotalMilliseconds);

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

    private static TimeSpan CalculateBackoffDelay(int attempt, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        // Exponential backoff with jitter
        var exponentialDelay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * 1000);
        var totalDelay = exponentialDelay + jitter;

        return totalDelay > maxDelay ? maxDelay : totalDelay;
    }
}

// Request/Response models for Ollama API
internal record OllamaGenerateRequest
{
    public required string Model { get; init; }
    public required string Prompt { get; init; }
    public float Temperature { get; init; } = 0.0f;
    public bool Stream { get; init; } = false;
    public string? Format { get; init; }
}

internal record OllamaGenerateResponse
{
    public string? Response { get; init; }
    public bool Done { get; init; }
    public OllamaModelInfo? ModelInfo { get; init; }
}

internal record OllamaModelInfo
{
    public string? Name { get; init; }
    public DateTime? ModifiedAt { get; init; }
    public long? Size { get; init; }
}

internal record ClassificationResponse
{
    public string Label { get; init; } = string.Empty;
    public double Confidence { get; set; }
    public string? Reason { get; init; }
}

// Public result model
public record ClassificationResult
{
    public bool IsPhishing { get; init; }
    public double Confidence { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string RawResponse { get; init; } = string.Empty;
    public bool IsError { get; init; }
    public string? ErrorMessage { get; init; }

    public static ClassificationResult Error(string errorMessage) =>
        new() { IsError = true, ErrorMessage = errorMessage };
}
