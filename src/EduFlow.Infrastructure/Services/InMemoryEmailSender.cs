using System.Collections.Concurrent;
using EduFlow.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace EduFlow.Infrastructure.Services;

public sealed class InMemoryEmailStore
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public IReadOnlyList<EmailMessage> Messages => _messages.ToArray();

    public void Add(EmailMessage message) => _messages.Enqueue(message);

    public EmailMessage? FindLatest(string recipientEmail, string? metadataType = null)
    {
        var normalized = recipientEmail.Trim().ToLowerInvariant();
        return _messages
            .Reverse()
            .FirstOrDefault(m =>
                m.To.Equals(normalized, StringComparison.OrdinalIgnoreCase)
                && (metadataType is null
                    || (m.Metadata is not null
                        && m.Metadata.TryGetValue("Type", out var type)
                        && type == metadataType)));
    }

    public void Clear()
    {
        while (_messages.TryDequeue(out _))
        {
        }
    }
}

public sealed class InMemoryEmailSender(
    InMemoryEmailStore store,
    ILogger<InMemoryEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        store.Add(message);
        logger.LogInformation("Email queued to {To} with subject {Subject}", message.To, message.Subject);
        return Task.CompletedTask;
    }
}
