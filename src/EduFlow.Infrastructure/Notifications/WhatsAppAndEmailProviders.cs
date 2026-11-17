using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EduFlow.Infrastructure.Notifications;

public sealed record SentWhatsAppEntry(
    string To,
    string Message,
    string? TemplateName,
    string? LanguageCode,
    Guid? TenantId,
    string? IdempotencyKey,
    string ProviderMessageId,
    DateTimeOffset SentAt);

public sealed class FakeWhatsAppStore
{
    private readonly ConcurrentQueue<SentWhatsAppEntry> _messages = new();

    public bool ForceAllFailures { get; set; }

    public IReadOnlyList<SentWhatsAppEntry> Messages => _messages.ToArray();

    public void Add(SentWhatsAppEntry entry) => _messages.Enqueue(entry);

    public bool ShouldFail(string recipient) =>
        ForceAllFailures || recipient.Contains("FAIL", StringComparison.OrdinalIgnoreCase);

    public void Clear()
    {
        ForceAllFailures = false;
        while (_messages.TryDequeue(out _))
        {
        }
    }
}

public sealed class FakeWhatsAppProvider(
    FakeWhatsAppStore store,
    ILogger<FakeWhatsAppProvider> logger) : IWhatsAppProvider
{
    public string ProviderName => "FakeWhatsApp";

    public Task<WhatsAppSendResult> SendAsync(WhatsAppSendRequest request, CancellationToken cancellationToken = default)
    {
        if (store.ShouldFail(request.To))
        {
            return Task.FromResult(new WhatsAppSendResult(
                Succeeded: false,
                Provider: ProviderName,
                ErrorCode: "WHATSAPP_ERROR",
                ErrorMessage: $"Simulated WhatsApp failure for {request.To}"));
        }

        var msgId = $"fake-wa-{Guid.NewGuid():N}";
        store.Add(new SentWhatsAppEntry(
            request.To,
            request.Message,
            request.TemplateName,
            request.LanguageCode,
            request.TenantId,
            request.IdempotencyKey,
            msgId,
            DateTimeOffset.UtcNow));

        logger.LogInformation("FakeWhatsAppProvider sent message to {Recipient} ({MessageId})", request.To, msgId);

        return Task.FromResult(new WhatsAppSendResult(
            Succeeded: true,
            Provider: ProviderName,
            ProviderMessageId: msgId));
    }
}

public sealed class MetaCloudWhatsAppProvider(
    HttpClient httpClient,
    IOptions<NotificationOptions> options,
    ILogger<MetaCloudWhatsAppProvider> logger) : IWhatsAppProvider
{
    public string ProviderName => "WhatsAppCloudApi";

    public async Task<WhatsAppSendResult> SendAsync(WhatsAppSendRequest request, CancellationToken cancellationToken = default)
    {
        var cfg = options.Value.WhatsApp.CloudApi;
        if (string.IsNullOrWhiteSpace(cfg.PhoneNumberId) || string.IsNullOrWhiteSpace(cfg.AccessToken))
        {
            return new WhatsAppSendResult(
                Succeeded: false,
                Provider: ProviderName,
                ErrorCode: "MISSING_CREDENTIALS",
                ErrorMessage: "WhatsApp Cloud API PhoneNumberId or AccessToken is not configured.");
        }

        var url = $"{cfg.Endpoint.TrimEnd('/')}/{cfg.PhoneNumberId}/messages";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.AccessToken);
        httpRequest.Content = JsonContent.Create(new
        {
            messaging_product = "whatsapp",
            to = NotifyLkSmsProvider.NormalizeSriLankanNumber(request.To),
            type = "text",
            text = new { body = request.Message }
        });

        try
        {
            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("WhatsApp Cloud API returned HTTP {StatusCode}: {Body}", (int)response.StatusCode, err);
                return new WhatsAppSendResult(
                    Succeeded: false,
                    Provider: ProviderName,
                    ErrorCode: $"HTTP_{(int)response.StatusCode}",
                    ErrorMessage: string.IsNullOrWhiteSpace(err) ? "WhatsApp Cloud API request failed." : err);
            }

            return new WhatsAppSendResult(
                Succeeded: true,
                Provider: ProviderName,
                ProviderMessageId: $"wacloud-{Guid.NewGuid():N}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WhatsApp Cloud API error for {Recipient}", request.To);
            return new WhatsAppSendResult(
                Succeeded: false,
                Provider: ProviderName,
                ErrorCode: "NETWORK_ERROR",
                ErrorMessage: ex.Message);
        }
    }
}

public sealed class ConfigurableEmailSender(
    InMemoryEmailStore inMemoryStore,
    HttpClient httpClient,
    IOptions<NotificationOptions> options,
    ILogger<ConfigurableEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var emailCfg = options.Value.Email;
        if (message.To.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Simulated email failure for {message.To}");
        }

        if (string.Equals(emailCfg.Provider, "Brevo", StringComparison.OrdinalIgnoreCase))
        {
            await SendViaBrevoAsync(message, emailCfg, cancellationToken);
            return;
        }

        if (string.Equals(emailCfg.Provider, "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            await SendViaSmtpAsync(message, emailCfg, cancellationToken);
            return;
        }

        inMemoryStore.Add(message);
        logger.LogInformation("Email stored in InMemoryEmailStore for {To} ({Subject})", message.To, message.Subject);
    }

    private async Task SendViaBrevoAsync(EmailMessage message, EmailProviderOptions cfg, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cfg.Brevo.ApiKey))
        {
            throw new InvalidOperationException("Brevo API key is not configured.");
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, cfg.Brevo.Endpoint);
        req.Headers.Add("api-key", cfg.Brevo.ApiKey);
        req.Content = JsonContent.Create(new
        {
            sender = new { name = cfg.FromName, email = cfg.FromEmail },
            to = new[] { new { email = message.To } },
            subject = message.Subject,
            htmlContent = message.HtmlBody,
            textContent = message.PlainTextBody ?? message.HtmlBody
        });

        using var res = await httpClient.SendAsync(req, cancellationToken);
        res.EnsureSuccessStatusCode();
    }

    private static async Task SendViaSmtpAsync(EmailMessage message, EmailProviderOptions cfg, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cfg.Smtp.Host))
        {
            throw new InvalidOperationException("SMTP host is not configured.");
        }

        using var smtp = new SmtpClient(cfg.Smtp.Host, cfg.Smtp.Port)
        {
            EnableSsl = cfg.Smtp.EnableSsl
        };

        if (!string.IsNullOrWhiteSpace(cfg.Smtp.Username))
        {
            smtp.Credentials = new NetworkCredential(cfg.Smtp.Username, cfg.Smtp.Password);
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(cfg.FromEmail, cfg.FromName),
            Subject = message.Subject,
            Body = message.HtmlBody,
            IsBodyHtml = true
        };
        mail.To.Add(message.To);

        await smtp.SendMailAsync(mail, cancellationToken);
    }
}
