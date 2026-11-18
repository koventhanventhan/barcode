using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Common.Security;
using EduFlow.Application.Notifications;
using Microsoft.Extensions.Logging;

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
    public decimal Balance { get; set; } = 5000.00m;
    public bool IsActive { get; set; } = true;

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
        Balance = 5000.00m;
        IsActive = true;
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
            logger.LogWarning("FakeSmsProvider simulated failure for recipient {Recipient}", PiiRedactor.MaskPhone(request.To));
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

        logger.LogInformation("FakeSmsProvider sent SMS to {Recipient} ({MessageId})", PiiRedactor.MaskPhone(request.To), msgId);

        return Task.FromResult(new SmsSendResult(
            Succeeded: true,
            Provider: ProviderName,
            ProviderMessageId: msgId));
    }

    public Task<SmsGatewayBalanceResult> GetBalanceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new SmsGatewayBalanceResult(
            Succeeded: true,
            Provider: ProviderName,
            IsActive: store.IsActive,
            Balance: store.Balance,
            Currency: "LKR"));
}

public sealed class NotifyLkSmsProvider(
    HttpClient httpClient,
    ISmsGatewayConfigProvider configProvider,
    ISmsSegmentCalculator segmentCalculator,
    ILogger<NotifyLkSmsProvider> logger) : ISmsProvider
{
    public string ProviderName => "Notify.lk";

    public async Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var cfg = await configProvider.GetEffectiveConfigAsync(cancellationToken);

        var normalized = SriLankanPhoneNormalizer.NormalizeForGateway(request.To);
        if (!normalized.IsValid || normalized.Canonical947 is null)
        {
            var errMsg = normalized.ErrorMessage ?? "Invalid recipient mobile number.";
            await configProvider.RecordErrorAsync(
                ProviderName,
                "Send",
                PiiRedactor.MaskPhone(request.To),
                normalized.ErrorCode ?? "INVALID_NUMBER",
                errMsg,
                request.TenantId,
                cancellationToken);

            return new SmsSendResult(
                Succeeded: false,
                Provider: ProviderName,
                ErrorCode: normalized.ErrorCode ?? "INVALID_NUMBER",
                ErrorMessage: errMsg);
        }

        if (cfg.TestMode && !SriLankanPhoneNormalizer.IsInAllowList(normalized.Canonical947, cfg.TestAllowList))
        {
            logger.LogInformation(
                "Notify.lk TestMode skipped SMS to {Recipient} (not in TestAllowList).",
                PiiRedactor.MaskPhone(normalized.Canonical947));
            return new SmsSendResult(
                Succeeded: false,
                Provider: ProviderName,
                ErrorCode: "TEST_MODE_SKIPPED",
                ErrorMessage: "Skipped in TestMode because recipient is not in TestAllowList.",
                IsSkipped: true,
                SkipReason: "Skipped:TestMode");
        }

        if (string.IsNullOrWhiteSpace(cfg.UserId) || string.IsNullOrWhiteSpace(cfg.ApiKey))
        {
            const string msg = "Notify.lk UserId or ApiKey is not configured.";
            await configProvider.RecordErrorAsync(
                ProviderName,
                "Send",
                PiiRedactor.MaskPhone(normalized.Canonical947),
                "MISSING_CREDENTIALS",
                msg,
                request.TenantId,
                cancellationToken);

            return new SmsSendResult(
                Succeeded: false,
                Provider: ProviderName,
                ErrorCode: "MISSING_CREDENTIALS",
                ErrorMessage: msg);
        }

        var senderId = await configProvider.ResolveTenantSenderIdAsync(
            request.TenantId,
            request.IsPromotional,
            request.SenderId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(senderId))
        {
            const string msg = "Notify.lk Sender ID is not configured.";
            await configProvider.RecordErrorAsync(
                ProviderName,
                "Send",
                PiiRedactor.MaskPhone(normalized.Canonical947),
                "INVALID_SENDER",
                msg,
                request.TenantId,
                cancellationToken);

            return new SmsSendResult(
                Succeeded: false,
                Provider: ProviderName,
                ErrorCode: "INVALID_SENDER",
                ErrorMessage: msg);
        }

        var segCalc = segmentCalculator.Calculate(request.Message);
        var fields = new Dictionary<string, string>
        {
            ["user_id"] = cfg.UserId,
            ["api_key"] = cfg.ApiKey,
            ["sender_id"] = senderId,
            ["to"] = normalized.Canonical947,
            ["message"] = request.Message
        };
        if (segCalc.IsUnicode)
        {
            fields["type"] = "unicode";
        }

        var sendUrl = ResolveEndpointUrl(cfg.BaseUrl, "send");
        try
        {
            using var content = new FormUrlEncodedContent(fields);
            using var response = await httpClient.PostAsync(sendUrl, content, cancellationToken);
            var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = ParseSendResponse(response.StatusCode, rawBody, ProviderName, cfg.ApiKey);

            if (!parsed.Succeeded)
            {
                logger.LogWarning(
                    "Notify.lk send failed ({ErrorCode}) for {Recipient}: {Message}",
                    parsed.ErrorCode,
                    PiiRedactor.MaskPhone(normalized.Canonical947),
                    parsed.ErrorMessage);

                await configProvider.RecordErrorAsync(
                    ProviderName,
                    "Send",
                    PiiRedactor.MaskPhone(normalized.Canonical947),
                    parsed.ErrorCode ?? "GATEWAY_ERROR",
                    parsed.ErrorMessage ?? "Notify.lk request failed.",
                    request.TenantId,
                    cancellationToken);
            }

            return parsed;
        }
        catch (Exception ex)
        {
            var safeMessage = PiiRedactor.RedactSecretValues(ex.Message, cfg.ApiKey);
            logger.LogError(
                "Notify.lk send exception for {Recipient}: {Message}",
                PiiRedactor.MaskPhone(normalized.Canonical947),
                safeMessage);

            await configProvider.RecordErrorAsync(
                ProviderName,
                "Send",
                PiiRedactor.MaskPhone(normalized.Canonical947),
                "NETWORK_ERROR",
                safeMessage,
                request.TenantId,
                cancellationToken);

            return new SmsSendResult(
                Succeeded: false,
                Provider: ProviderName,
                ErrorCode: "NETWORK_ERROR",
                ErrorMessage: safeMessage);
        }
    }

    public async Task<SmsGatewayBalanceResult> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        var cfg = await configProvider.GetEffectiveConfigAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(cfg.UserId) || string.IsNullOrWhiteSpace(cfg.ApiKey))
        {
            return new SmsGatewayBalanceResult(
                Succeeded: false,
                Provider: ProviderName,
                ErrorCode: "MISSING_CREDENTIALS",
                ErrorMessage: "Notify.lk UserId or ApiKey is not configured.");
        }

        var statusUrl = $"{ResolveEndpointUrl(cfg.BaseUrl, "status")}?user_id={Uri.EscapeDataString(cfg.UserId)}&api_key={Uri.EscapeDataString(cfg.ApiKey)}";
        try
        {
            using var response = await httpClient.GetAsync(statusUrl, cancellationToken);
            var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = ParseBalanceResponse(response.StatusCode, rawBody, ProviderName, cfg.ApiKey);

            if (!parsed.Succeeded)
            {
                await configProvider.RecordErrorAsync(
                    ProviderName,
                    "BalanceCheck",
                    null,
                    parsed.ErrorCode ?? "GATEWAY_ERROR",
                    parsed.ErrorMessage ?? "Failed to fetch Notify.lk balance.",
                    null,
                    cancellationToken);
            }

            return parsed;
        }
        catch (Exception ex)
        {
            var safeMessage = PiiRedactor.RedactSecretValues(ex.Message, cfg.ApiKey);
            logger.LogError("Notify.lk status check failed: {Message}", safeMessage);
            await configProvider.RecordErrorAsync(
                ProviderName,
                "BalanceCheck",
                null,
                "NETWORK_ERROR",
                safeMessage,
                null,
                cancellationToken);

            return new SmsGatewayBalanceResult(
                Succeeded: false,
                Provider: ProviderName,
                ErrorCode: "NETWORK_ERROR",
                ErrorMessage: safeMessage);
        }
    }

    public static string ResolveEndpointUrl(string? baseUrl, string action)
    {
        var effective = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://app.notify.lk/api/v1"
            : baseUrl.Trim().TrimEnd('/');

        if (effective.EndsWith("/send", StringComparison.OrdinalIgnoreCase)
            || effective.EndsWith("/status", StringComparison.OrdinalIgnoreCase))
        {
            var lastSlash = effective.LastIndexOf('/');
            effective = effective[..lastSlash];
        }

        return $"{effective}/{action}";
    }

    public static SmsSendResult ParseSendResponse(
        HttpStatusCode statusCode,
        string? rawBody,
        string providerName = "Notify.lk",
        params string?[] secretsToRedact)
    {
        var safeBody = PiiRedactor.RedactSecretValues(rawBody, secretsToRedact);
        var isHttpSuccess = (int)statusCode is >= 200 and < 300;

        if (!string.IsNullOrWhiteSpace(safeBody))
        {
            try
            {
                using var doc = JsonDocument.Parse(safeBody);
                var root = doc.RootElement;
                var status = root.TryGetProperty("status", out var statusEl)
                    ? statusEl.GetString()
                    : null;

                if (isHttpSuccess && string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
                {
                    string? providerMessageId = null;
                    int? gatewaySegments = null;
                    decimal? gatewayCost = null;

                    if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
                    {
                        if (dataEl.TryGetProperty("message_id", out var msgIdEl))
                        {
                            providerMessageId = msgIdEl.ToString();
                        }
                        else if (dataEl.TryGetProperty("id", out var idEl))
                        {
                            providerMessageId = idEl.ToString();
                        }

                        if (dataEl.TryGetProperty("segments", out var segEl) && segEl.TryGetInt32(out var seg))
                        {
                            gatewaySegments = seg;
                        }

                        if (dataEl.TryGetProperty("cost", out var costEl) && costEl.TryGetDecimal(out var cost))
                        {
                            gatewayCost = cost;
                        }
                    }

                    return new SmsSendResult(
                        Succeeded: true,
                        Provider: providerName,
                        ProviderMessageId: string.IsNullOrWhiteSpace(providerMessageId)
                            ? $"notifylk-{Guid.NewGuid():N}"
                            : providerMessageId,
                        GatewaySegments: gatewaySegments,
                        GatewayCost: gatewayCost);
                }

                var errorMessage = ExtractErrorMessage(root) ?? safeBody;
                errorMessage = PiiRedactor.RedactSecretValues(errorMessage, secretsToRedact);
                var errorCode = ClassifyErrorCode(statusCode, errorMessage);
                return new SmsSendResult(
                    Succeeded: false,
                    Provider: providerName,
                    ErrorCode: errorCode,
                    ErrorMessage: string.IsNullOrWhiteSpace(errorMessage) ? "Notify.lk request failed." : errorMessage);
            }
            catch (JsonException)
            {
                // Fall through to status code handling below.
            }
        }

        if (isHttpSuccess)
        {
            return new SmsSendResult(
                Succeeded: true,
                Provider: providerName,
                ProviderMessageId: $"notifylk-{Guid.NewGuid():N}");
        }

        var fallbackErr = string.IsNullOrWhiteSpace(safeBody)
            ? $"Notify.lk returned HTTP {(int)statusCode}."
            : safeBody;
        return new SmsSendResult(
            Succeeded: false,
            Provider: providerName,
            ErrorCode: ClassifyErrorCode(statusCode, fallbackErr),
            ErrorMessage: fallbackErr);
    }

    public static SmsGatewayBalanceResult ParseBalanceResponse(
        HttpStatusCode statusCode,
        string? rawBody,
        string providerName = "Notify.lk",
        params string?[] secretsToRedact)
    {
        var safeBody = PiiRedactor.RedactSecretValues(rawBody, secretsToRedact);
        var isHttpSuccess = (int)statusCode is >= 200 and < 300;

        if (!string.IsNullOrWhiteSpace(safeBody))
        {
            try
            {
                using var doc = JsonDocument.Parse(safeBody);
                var root = doc.RootElement;
                var status = root.TryGetProperty("status", out var statusEl)
                    ? statusEl.GetString()
                    : null;

                if (isHttpSuccess
                    && string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)
                    && root.TryGetProperty("data", out var dataEl)
                    && dataEl.ValueKind == JsonValueKind.Object)
                {
                    bool? active = null;
                    if (dataEl.TryGetProperty("active", out var activeEl))
                    {
                        active = activeEl.ValueKind switch
                        {
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.String when bool.TryParse(activeEl.GetString(), out var b) => b,
                            _ => null
                        };
                    }

                    decimal? balance = null;
                    if (dataEl.TryGetProperty("acc_balance", out var balEl))
                    {
                        if (balEl.ValueKind == JsonValueKind.Number && balEl.TryGetDecimal(out var d))
                        {
                            balance = d;
                        }
                        else if (balEl.ValueKind == JsonValueKind.String
                            && decimal.TryParse(balEl.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedBal))
                        {
                            balance = parsedBal;
                        }
                    }

                    return new SmsGatewayBalanceResult(
                        Succeeded: true,
                        Provider: providerName,
                        IsActive: active,
                        Balance: balance,
                        Currency: "LKR");
                }

                var errorMessage = ExtractErrorMessage(root) ?? safeBody;
                errorMessage = PiiRedactor.RedactSecretValues(errorMessage, secretsToRedact);
                return new SmsGatewayBalanceResult(
                    Succeeded: false,
                    Provider: providerName,
                    ErrorCode: ClassifyErrorCode(statusCode, errorMessage),
                    ErrorMessage: string.IsNullOrWhiteSpace(errorMessage) ? "Failed to read Notify.lk balance." : errorMessage);
            }
            catch (JsonException)
            {
                // Fall through to HTTP status error.
            }
        }

        var fallbackErr = string.IsNullOrWhiteSpace(safeBody)
            ? $"Notify.lk status check returned HTTP {(int)statusCode}."
            : safeBody;
        return new SmsGatewayBalanceResult(
            Succeeded: false,
            Provider: providerName,
            ErrorCode: ClassifyErrorCode(statusCode, fallbackErr),
            ErrorMessage: fallbackErr);
    }

    private static string? ExtractErrorMessage(JsonElement root)
    {
        if (root.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
        {
            var msg = msgEl.GetString();
            if (!string.IsNullOrWhiteSpace(msg))
            {
                return msg;
            }
        }

        if (root.TryGetProperty("errors", out var errorsEl))
        {
            if (errorsEl.ValueKind == JsonValueKind.String)
            {
                return errorsEl.GetString();
            }

            if (errorsEl.ValueKind == JsonValueKind.Array)
            {
                var parts = errorsEl.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                var joined = string.Join("; ", parts);
                if (!string.IsNullOrWhiteSpace(joined))
                {
                    return joined;
                }
            }

            if (errorsEl.ValueKind == JsonValueKind.Object)
            {
                var parts = new List<string>();
                foreach (var prop in errorsEl.EnumerateObject())
                {
                    parts.Add(prop.Value.ValueKind == JsonValueKind.Array
                        ? $"{prop.Name}: {string.Join(", ", prop.Value.EnumerateArray().Select(x => x.ToString()))}"
                        : $"{prop.Name}: {prop.Value}");
                }

                if (parts.Count > 0)
                {
                    return string.Join("; ", parts);
                }
            }
        }

        if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.String)
        {
            return dataEl.GetString();
        }

        return null;
    }

    private static string ClassifyErrorCode(HttpStatusCode statusCode, string? message)
    {
        var msg = message ?? string.Empty;
        if (msg.Contains("balance", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("insufficient", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("credit", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("fund", StringComparison.OrdinalIgnoreCase))
        {
            return "INSUFFICIENT_BALANCE";
        }

        if (msg.Contains("sender", StringComparison.OrdinalIgnoreCase))
        {
            return "INVALID_SENDER";
        }

        if (msg.Contains("number", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("mobile", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("phone", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("recipient", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("invalid to", StringComparison.OrdinalIgnoreCase))
        {
            return "INVALID_NUMBER";
        }

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            || msg.Contains("api_key", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("api key", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("user_id", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("credential", StringComparison.OrdinalIgnoreCase))
        {
            return "INVALID_CREDENTIALS";
        }

        return (int)statusCode is >= 400 and < 600
            ? $"HTTP_{(int)statusCode}"
            : "GATEWAY_ERROR";
    }

    public static string NormalizeSriLankanNumber(string phone)
    {
        var res = SriLankanPhoneNormalizer.NormalizeForGateway(phone);
        if (res.IsValid && res.Canonical947 is not null)
        {
            return res.Canonical947;
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("0", StringComparison.Ordinal) && digits.Length == 10)
        {
            return "94" + digits[1..];
        }

        return digits;
    }
}

public sealed class RuntimeSmsProvider(
    ISmsGatewayConfigProvider configProvider,
    FakeSmsProvider fakeProvider,
    NotifyLkSmsProvider notifyLkProvider) : ISmsProvider
{
    public string ProviderName => ResolveAsync().GetAwaiter().GetResult().ProviderName;

    public async Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var provider = await ResolveAsync(cancellationToken);
        return await provider.SendAsync(request, cancellationToken);
    }

    public async Task<SmsGatewayBalanceResult> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        var provider = await ResolveAsync(cancellationToken);
        return await provider.GetBalanceAsync(cancellationToken);
    }

    private async Task<ISmsProvider> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var cfg = await configProvider.GetEffectiveConfigAsync(cancellationToken);
        return string.Equals(cfg.Provider, "NotifyLk", StringComparison.OrdinalIgnoreCase)
            ? notifyLkProvider
            : fakeProvider;
    }
}
