using Google.Apis.Gmail.v1.Data;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace AiGmailLabeler.Core.Services;

public class MimeTextExtractor
{
    private readonly ILogger<MimeTextExtractor> _logger;

    public MimeTextExtractor(ILogger<MimeTextExtractor> logger)
    {
        _logger = logger;
    }

    public string ExtractText(Message message, int maxSizeKb)
    {
        var payload = message.Payload;
        if (payload == null)
        {
            _logger.LogDebug("Message {MessageId} has no payload", message.Id);
            return string.Empty;
        }

        var bodyText = ExtractTextFromPart(payload);

        // Clamp to maximum configured size
        var maxBytes = maxSizeKb * 1024;
        if (bodyText.Length > maxBytes)
        {
            bodyText = bodyText[..maxBytes];
            _logger.LogDebug("Clamped message {MessageId} body to {MaxBytes} bytes", message.Id, maxBytes);
        }

        _logger.LogDebug("Extracted {TextLength} characters from message {MessageId}", bodyText.Length, message.Id);
        return bodyText;
    }

    private string ExtractTextFromPart(MessagePart part)
    {
        var text = new StringBuilder();

        // If this part has body data
        if (part.Body?.Data != null)
        {
            try
            {
                var decoded = DecodeBase64Url(part.Body.Data);
                var content = Encoding.UTF8.GetString(decoded);

                // Strip HTML if this is HTML content
                if (IsHtmlContent(part.MimeType))
                {
                    content = StripHtml(content);
                }

                text.AppendLine(content);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decode body data for part with MIME type: {MimeType}", part.MimeType);
            }
        }

        // Recursively process multipart content
        if (part.Parts != null)
        {
            foreach (var subPart in part.Parts)
            {
                if (IsTextPart(subPart))
                {
                    text.AppendLine(ExtractTextFromPart(subPart));
                }
            }
        }

        return text.ToString().Trim();
    }

    private static byte[] DecodeBase64Url(string base64Url)
    {
        // Gmail uses base64url encoding (RFC 4648 Section 5)
        var base64 = base64Url.Replace('-', '+').Replace('_', '/');

        // Add padding if necessary
        var paddingLength = 4 - (base64.Length % 4);
        if (paddingLength != 4)
        {
            base64 += new string('=', paddingLength);
        }

        return Convert.FromBase64String(base64);
    }

    private static bool IsTextPart(MessagePart part)
    {
        return part.MimeType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsHtmlContent(string? mimeType)
    {
        return mimeType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true;
    }

    private string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;

        try
        {
            // Remove HTML tags
            var tagPattern = @"<[^>]*>";
            var withoutTags = Regex.Replace(html, tagPattern, " ");

            // Decode common HTML entities
            var decoded = DecodeHtmlEntities(withoutTags);

            // Normalize whitespace
            var normalized = Regex.Replace(decoded, @"\s+", " ");

            return normalized.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to strip HTML from content, returning as-is");
            return html;
        }
    }

    private static string DecodeHtmlEntities(string text)
    {
        // Decode common HTML entities
        var decoded = text
            .Replace("&nbsp;", " ")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&#x27;", "'")
            .Replace("&#x2F;", "/")
            .Replace("&#x60;", "`")
            .Replace("&#x3D;", "=");

        // Decode numeric HTML entities (basic support)
        decoded = Regex.Replace(decoded, @"&#(\d+);", match =>
        {
            if (int.TryParse(match.Groups[1].Value, out var code) && code >= 32 && code <= 126)
            {
                return ((char)code).ToString();
            }
            return match.Value;
        });

        // Decode hex HTML entities (basic support)
        decoded = Regex.Replace(decoded, @"&#x([0-9A-Fa-f]+);", match =>
        {
            if (int.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out var code) &&
                code >= 32 && code <= 126)
            {
                return ((char)code).ToString();
            }
            return match.Value;
        });

        return decoded;
    }

    public bool HasUsableText(string text, int minLength = 10)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Remove whitespace and check if we have enough meaningful content
        var trimmed = text.Trim();
        return trimmed.Length >= minLength && !IsOnlySpecialCharacters(trimmed);
    }

    private static bool IsOnlySpecialCharacters(string text)
    {
        // Check if text contains mostly non-alphanumeric characters
        var alphaNumericCount = text.Count(char.IsLetterOrDigit);
        return alphaNumericCount < text.Length * 0.3; // Less than 30% alphanumeric
    }
}
