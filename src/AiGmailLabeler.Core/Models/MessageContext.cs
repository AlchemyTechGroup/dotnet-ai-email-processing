using Google.Apis.Gmail.v1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiGmailLabeler.Core.Models;

public record MessageContext
{
    public required GmailService GmailService { get; init; }
    public required string MessageId { get; init; }
    public required string ThreadId { get; init; }
    public required MessageHeaders Headers { get; init; }
    public required string BodyText { get; init; }
    public required IConfiguration Configuration { get; init; }
    public required ILogger Logger { get; init; }
    public required CancellationToken CancellationToken { get; init; }
}

public record MessageHeaders
{
    public string? Subject { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public DateTime? Date { get; init; }
}
