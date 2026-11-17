using System.Collections.Concurrent;
using System.Net.Http.Json;
using EduFlow.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EduFlow.Infrastructure.Notifications;

public sealed record SentSmsEntry(
    string To,
    string Message,
    string? SenderId,
    Guid? TenantId,
    string? IdempotencyKey,
    string ProviderMessageId,
    DateTimeOffset SentAt);

public sealed class FakeSmsStore
{
    private readonly ConcurrentQueue<SentSmsEntry> _messages = new();
    private readonly ConcurrentDictionary<string, bool> _failingRecipients = new(StringComparer.OrdinalIgnoreCase);

    public bool ForceAllFailures { get; set; }

    public IReadOnlyList<SentSmsEntry> Messages => _messages.ToArray();

    public void Add(SentSmsEntry entry) => _messages.Enqueue(entry);

    public void SetRecipientFailure(string recipient, bool shouldFail)
    {
        if (shouldFail)
        {
            _failingRecipients[recipient.Trim()] = true;
        }
        else
        {
            _failingRecipients.TryRemove(recipient.Trim(), out _);
        }
    }

    public bool ShouldFail(string recipient)
    {
        if (ForceAllFailures)
        {
            return true;
        }

        var trimmed = recipient.Trim();
        if (trimmed.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return _failingRecipients.ContainsKey(trimmed);
    }

    public void Clear()
    {
        ForceAllFailures = false;
        _failingRecipients.Clear();
        while (_messages.TryDequeue(out _))
        {
        }
    }
}

public sealed class FakeSmsProvider(
    FakeSmsStore store,
    ILogger<FakeSmsProvider> logger) : ISmsProvider
{
    public string ProviderName => "FakeSms";

    public Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        if (store.ShouldFail(request.To))
        {
            logger.LogWarning("FakeSmsProvider simulated failure for recipient {Recipient}", request.To);
            return Task.FromResult(new SmsSendResult(
                Succeeded: false,
                Provider: ProviderName,
                ProviderMessageId: null,
                ErrorCode: "GATEWAY_ERROR",
                ErrorMessage: $"Simulated SMS failure for {request.To}"));
        }

        var msgId = $"fake-sms-{Guid.NewGuid():N}";
        store.Add(new SentSmsEntry(
            request.To,
            request.Message,
            request.SenderId,
            request.TenantId,
            request.IdempotencyKey,
            msgId,
            DateTimeOffset.UtcNow));

        logger.LogInformation("FakeSmsProvider sent SMS to {Recipient} ({MessageId})", request.To, msgId);

        return Task.FromResult(new SmsSendResult(
            Succeeded: true,
            Provider: ProviderName,
            ProviderMessageId: msgId));
    }
}

public sealed class NotifyLkSmsProvider(
    HttpClient httpClient,
    IOptions<NotificationOptions> options,
    ILogger<NotifyLkSmsProvider> logger) : ISmsProvider
{
    public string ProviderName => "Notify.lk";

    public async Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var cfg = options.Value.Sms.NotifyLk;
        if (string.IsNullOrWhiteSpace(cfg.UserId) || string.IsNullOrWhiteSpace(cfg.ApiKey))
        {
            return new SmsSendResult(
                Succeeded: false,
                Provider: ProviderName,
                ErrorCode: "MISSING_CREDENTIALS",
                ErrorMessage: "Notify.lk UserId or ApiKey is not configured.");
        }

        var normalizedPhone = NormalizeSriLankanNumber(request.To);
        var payload = new Dictionary<string, string>
        {
            ["user_id"] = cfg.UserId,
            ["api_key"] = cfg.ApiKey,
            ["sender_id"] = string.IsNullOrWhiteSpace(request.SenderId) ? cfg.SenderId : request.SenderId!,
            ["to"] = normalizedPhone,
            ["message"] = request.Message
        };

        try
        {
            using var response = await httpClient.PostAsJsonAsync(cfg.Endpoint, payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Notify.lk returned HTTP {StatusCode}: {Body}", (int)response.StatusCode, err);
                return new SmsSendResult(
                    Succeeded: false,
                    Provider: ProviderName,
                    ErrorCode: $"HTTP_{(int)response.StatusCode}",
                    ErrorMessage: string.IsNullOrWhiteSpace(err) ? "Notify.lk request failed." : err);
            }

            return new SmsSendResult(
                Succeeded: true,
                Provider: ProviderName,
                ProviderMessageId: $"notifylk-{Guid.NewGuid():N}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Notify.lk SMS send exception for {Recipient}", request.To);
            return new SmsSendResult(
                Succeeded: false,
                Provider: ProviderName,
                ErrorCode: "NETWORK_ERROR",
                ErrorMessage: ex.Message);
        }
    }

    public static string NormalizeSriLankanNumber(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("0") && digits.Length == 10)
        {
            return "94" + digits[1..];
        }

        return digits;
    }
}
