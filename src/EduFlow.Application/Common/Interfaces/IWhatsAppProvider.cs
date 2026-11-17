namespace EduFlow.Application.Common.Interfaces;

public sealed record WhatsAppSendRequest(
    string To,
    string Message,
    string? TemplateName = null,
    string? LanguageCode = null,
    Guid? TenantId = null,
    string? IdempotencyKey = null);

public sealed record WhatsAppSendResult(
    bool Succeeded,
    string Provider,
    string? ProviderMessageId = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public interface IWhatsAppProvider
{
    string ProviderName { get; }

    Task<WhatsAppSendResult> SendAsync(WhatsAppSendRequest request, CancellationToken cancellationToken = default);
}
