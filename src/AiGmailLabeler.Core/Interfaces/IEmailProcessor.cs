using AiGmailLabeler.Core.Models;

namespace AiGmailLabeler.Core.Interfaces;

public interface IEmailProcessor
{
    Task<ProcessorOutcome> ProcessAsync(MessageContext context, CancellationToken cancellationToken = default);
}
