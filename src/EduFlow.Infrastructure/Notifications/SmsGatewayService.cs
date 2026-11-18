using System.Net;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Common.Security;
using EduFlow.Application.Notifications;
using EduFlow.Domain;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EduFlow.Infrastructure.Notifications;

public sealed class SmsGatewayConfigProvider(
    IAppDbContext db,
    ITenantContext tenantContext,
    IOptions<NotificationOptions> options,
    IDataProtectionProvider dataProtectionProvider,
    IHostEnvironment hostEnvironment,
    ILogger<SmsGatewayConfigProvider> logger) : ISmsGatewayConfigProvider
{
    private const string ProtectorPurpose = "Admino.SmsGateway.ApiKey.v1";
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public async Task<EffectiveSmsGatewayConfig> GetEffectiveConfigAsync(CancellationToken cancellationToken = default)
    {
        var smsOpt = options.Value.Sms;
        var row = await db.PlatformSmsSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => !s.IsDeleted && s.Key == "default", cancellationToken);

        var provider = Coalesce(row?.Provider, smsOpt.Provider, "Fake");
        var userId = Coalesce(row?.UserId, smsOpt.UserId);
        var senderId = Coalesce(row?.SenderId, smsOpt.SenderId, "Admino");
        var promoSenderId = FirstNonEmpty(row?.PromotionalSenderId, smsOpt.PromotionalSenderId);
        var baseUrl = Coalesce(row?.BaseUrl, smsOpt.BaseUrl, "https://app.notify.lk/api/v1");
        var testMode = row?.TestMode ?? smsOpt.TestMode ?? hostEnvironment.IsDevelopment();
        var allowListRaw = FirstNonEmpty(row?.TestAllowList, smsOpt.TestAllowList);
        var allowList = SriLankanPhoneNormalizer.ParseAllowList(allowListRaw);
        var threshold = row?.LowBalanceThreshold ?? smsOpt.LowBalanceThreshold;

        var apiKey = string.Empty;
        if (!string.IsNullOrWhiteSpace(row?.EncryptedApiKey))
        {
            try
            {
                apiKey = _protector.Unprotect(row.EncryptedApiKey);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to decrypt stored SMS gateway API key; falling back to configuration.");
            }
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = FirstNonEmpty(smsOpt.ApiKey) ?? string.Empty;
        }

        return new EffectiveSmsGatewayConfig(
            Provider: NormalizeProviderName(provider),
            UserId: userId,
            ApiKey: apiKey,
            SenderId: senderId,
            PromotionalSenderId: promoSenderId,
            BaseUrl: baseUrl,
            TestMode: testMode,
            TestAllowList: allowList,
            LowBalanceThreshold: threshold);
    }

    public async Task<string?> ResolveTenantSenderIdAsync(
        Guid? tenantId,
        bool isPromotional,
        string? explicitSenderId,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(explicitSenderId))
        {
            return explicitSenderId.Trim();
        }

        var cfg = await GetEffectiveConfigAsync(cancellationToken);

        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
        {
            var tenantSenderId = await db.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => !t.IsDeleted && t.Id == tenantId.Value)
                .Select(t => t.SmsSenderId)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(tenantSenderId))
            {
                return tenantSenderId.Trim();
            }
        }

        if (isPromotional && !string.IsNullOrWhiteSpace(cfg.PromotionalSenderId))
        {
            return cfg.PromotionalSenderId.Trim();
        }

        return cfg.SenderId;
    }

    public async Task RecordErrorAsync(
        string provider,
        string operation,
        string? recipient,
        string errorCode,
        string errorMessage,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cfg = await GetEffectiveConfigAsync(cancellationToken);
            var safeMessage = PiiRedactor.RedactSecretValues(errorMessage, cfg.ApiKey);
            if (safeMessage.Length > 1000)
            {
                safeMessage = safeMessage[..1000];
            }

            using (tenantContext.BeginSystemScope())
            {
                db.SmsGatewayErrorLogs.Add(new SmsGatewayErrorLog
                {
                    TenantId = tenantId,
                    Provider = string.IsNullOrWhiteSpace(provider) ? "Notify.lk" : provider,
                    Operation = string.IsNullOrWhiteSpace(operation) ? "Send" : operation,
                    Recipient = string.IsNullOrWhiteSpace(recipient) ? null : recipient,
                    ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? "GATEWAY_ERROR" : errorCode,
                    ErrorMessage = safeMessage,
                    OccurredAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist SMS gateway error log entry.");
        }
    }

    public static string NormalizeProviderName(string? provider)
    {
        if (string.Equals(provider?.Trim(), "NotifyLk", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider?.Trim(), "Notify.lk", StringComparison.OrdinalIgnoreCase))
        {
            return "NotifyLk";
        }

        return "Fake";
    }

    private static string Coalesce(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}

public sealed class SmsGatewayAdminService(
    IAppDbContext db,
    ITenantContext tenantContext,
    IAuditService auditService,
    ISmsGatewayConfigProvider configProvider,
    ISmsProvider smsProvider,
    ISmsSegmentCalculator segmentCalculator,
    IEmailSender emailSender,
    IMemoryCache cache,
    IDataProtectionProvider dataProtectionProvider,
    IOptions<NotificationOptions> options,
    IConfiguration configuration) : ISmsGatewayAdminService
{
    private const string ProtectorPurpose = "Admino.SmsGateway.ApiKey.v1";
    private const string BalanceCacheKey = "Admino:SmsGateway:Balance:v1";
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public async Task<SuperAdminSmsSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var cfg = await configProvider.GetEffectiveConfigAsync(cancellationToken);
        var row = await db.PlatformSmsSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => !s.IsDeleted && s.Key == "default", cancellationToken);

        var hasDbKey = !string.IsNullOrWhiteSpace(row?.EncryptedApiKey);
        var hasConfigKey = !string.IsNullOrWhiteSpace(options.Value.Sms.ApiKey);
        var apiKeySource = hasDbKey ? "Database" : hasConfigKey ? "Config" : "None";

        var errors = await db.SmsGatewayErrorLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => !e.IsDeleted)
            .OrderByDescending(e => e.OccurredAt)
            .Take(50)
            .Select(e => new SmsGatewayErrorDto(
                e.Id,
                e.TenantId,
                e.Provider,
                e.Operation,
                e.Recipient,
                e.ErrorCode,
                e.ErrorMessage,
                e.OccurredAt))
            .ToListAsync(cancellationToken);

        return new SuperAdminSmsSettingsDto(
            Provider: cfg.Provider,
            UserId: cfg.UserId,
            HasApiKey: hasDbKey || hasConfigKey,
            ApiKeySource: apiKeySource,
            SenderId: cfg.SenderId,
            PromotionalSenderId: cfg.PromotionalSenderId,
            BaseUrl: cfg.BaseUrl,
            TestMode: cfg.TestMode,
            TestAllowList: cfg.TestAllowList,
            LowBalanceThreshold: cfg.LowBalanceThreshold,
            LastLowBalanceAlertSentAt: row?.LastLowBalanceAlertSentAt,
            RecentErrors: errors);
    }

    public async Task<SuperAdminSmsSettingsDto> UpdateSettingsAsync(
        UpdateSuperAdminSmsSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var provider = SmsGatewayConfigProvider.NormalizeProviderName(request.Provider);
        if (request.LowBalanceThreshold < 0 || request.LowBalanceThreshold > 1_000_000m)
        {
            throw new DomainValidationException("INVALID_THRESHOLD", "Low-balance threshold must be between 0 and 1,000,000.");
        }

        var normalizedSenderId = NotificationService.NormalizeSenderId(request.SenderId) ?? "Admino";
        var normalizedPromoSenderId = NotificationService.NormalizeSenderId(request.PromotionalSenderId);
        var normalizedAllowList = string.Join(", ", SriLankanPhoneNormalizer.ParseAllowList(request.TestAllowList));
        var baseUrl = string.IsNullOrWhiteSpace(request.BaseUrl)
            ? "https://app.notify.lk/api/v1"
            : request.BaseUrl.Trim().TrimEnd('/');

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedUri)
            || (parsedUri.Scheme != Uri.UriSchemeHttps && parsedUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new DomainValidationException("INVALID_BASE_URL", "Base URL must be a valid http or https URL.");
        }

        using (tenantContext.BeginSystemScope())
        {
            var setting = await db.PlatformSmsSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => !s.IsDeleted && s.Key == "default", cancellationToken);

            if (setting is null)
            {
                setting = new PlatformSmsSetting { Key = "default" };
                db.PlatformSmsSettings.Add(setting);
            }

            setting.Provider = provider;
            setting.UserId = request.UserId?.Trim() ?? string.Empty;
            if (request.ClearApiKey)
            {
                setting.EncryptedApiKey = null;
            }
            else if (!string.IsNullOrWhiteSpace(request.ApiKey))
            {
                setting.EncryptedApiKey = _protector.Protect(request.ApiKey.Trim());
            }

            setting.SenderId = normalizedSenderId;
            setting.PromotionalSenderId = normalizedPromoSenderId;
            setting.BaseUrl = baseUrl;
            setting.TestMode = request.TestMode;
            setting.TestAllowList = normalizedAllowList;
            setting.LowBalanceThreshold = request.LowBalanceThreshold;
            setting.UpdatedByUserId = tenantContext.UserId;

            await auditService.LogAsync(
                action: "SuperAdmin.SmsSettingsUpdated",
                entityName: nameof(PlatformSmsSetting),
                entityId: setting.Id.ToString(),
                details: new
                {
                    Provider = provider,
                    UserId = setting.UserId,
                    SenderId = normalizedSenderId,
                    PromotionalSenderId = normalizedPromoSenderId,
                    BaseUrl = baseUrl,
                    TestMode = request.TestMode,
                    AllowListCount = SriLankanPhoneNormalizer.ParseAllowList(normalizedAllowList).Count,
                    LowBalanceThreshold = request.LowBalanceThreshold,
                    ApiKeyUpdated = !string.IsNullOrWhiteSpace(request.ApiKey),
                    request.ClearApiKey
                },
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        cache.Remove(BalanceCacheKey);
        return await GetSettingsAsync(cancellationToken);
    }

    public async Task<SmsGatewayBalanceDto> GetBalanceAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && cache.TryGetValue<SmsGatewayBalanceDto>(BalanceCacheKey, out var cached) && cached is not null)
        {
            return cached with { FromCache = true };
        }

        var cfg = await configProvider.GetEffectiveConfigAsync(cancellationToken);
        var result = await smsProvider.GetBalanceAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var isBelow = result.Succeeded
                      && result.Balance.HasValue
                      && result.Balance.Value < cfg.LowBalanceThreshold;

        var dto = new SmsGatewayBalanceDto(
            Succeeded: result.Succeeded,
            Provider: result.Provider,
            IsActive: result.IsActive,
            Balance: result.Balance,
            Currency: result.Currency,
            IsBelowThreshold: isBelow,
            LowBalanceThreshold: cfg.LowBalanceThreshold,
            CheckedAt: now,
            FromCache: false,
            ErrorCode: result.ErrorCode,
            ErrorMessage: result.ErrorMessage);

        if (result.Succeeded)
        {
            using (tenantContext.BeginSystemScope())
            {
                var setting = await db.PlatformSmsSettings
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(s => !s.IsDeleted && s.Key == "default", cancellationToken);
                if (setting is null)
                {
                    setting = new PlatformSmsSetting
                    {
                        Key = "default",
                        LowBalanceThreshold = cfg.LowBalanceThreshold
                    };
                    db.PlatformSmsSettings.Add(setting);
                }

                setting.LastKnownBalance = result.Balance;
                setting.LastGatewayActive = result.IsActive;
                setting.BalanceCheckedAt = now;
                await db.SaveChangesAsync(cancellationToken);
            }

            cache.Set(BalanceCacheKey, dto, TimeSpan.FromMinutes(5));
        }

        return dto;
    }

    public async Task<SuperAdminTestSmsResultDto> SendTestSmsAsync(
        SendSuperAdminTestSmsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new DomainValidationException("MESSAGE_REQUIRED", "Test SMS message cannot be empty.");
        }

        var phoneCheck = SriLankanPhoneNormalizer.NormalizeForGateway(request.To);
        if (!phoneCheck.IsValid || phoneCheck.Canonical947 is null)
        {
            throw new DomainValidationException(phoneCheck.ErrorCode ?? "INVALID_NUMBER", phoneCheck.ErrorMessage ?? "Invalid recipient mobile number.");
        }

        var senderOverride = NotificationService.NormalizeSenderId(request.SenderId);
        var message = request.Message.Trim();
        var calc = segmentCalculator.Calculate(message);

        var result = await smsProvider.SendAsync(
            new SmsSendRequest(
                To: phoneCheck.Canonical947,
                Message: message,
                SenderId: senderOverride,
                TenantId: null,
                IdempotencyKey: $"sa-test-{Guid.NewGuid():N}",
                IsPromotional: request.IsPromotional),
            cancellationToken);

        await auditService.LogAsync(
            action: "SuperAdmin.SmsTestSent",
            entityName: nameof(PlatformSmsSetting),
            entityId: "default",
            details: new
            {
                Recipient = PiiRedactor.MaskPhone(phoneCheck.Canonical947),
                result.Provider,
                result.Succeeded,
                result.IsSkipped,
                result.SkipReason,
                result.ErrorCode
            },
            cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new SuperAdminTestSmsResultDto(
            Succeeded: result.Succeeded,
            IsSkipped: result.IsSkipped,
            SkipReason: result.SkipReason,
            Provider: result.Provider,
            ProviderMessageId: result.ProviderMessageId,
            ErrorCode: result.ErrorCode,
            ErrorMessage: result.ErrorMessage,
            Segments: calc.Segments,
            GatewaySegments: result.GatewaySegments,
            GatewayCost: result.GatewayCost);
    }

    public async Task<LowBalanceCheckResultDto> CheckLowBalanceAndNotifyAsync(
        DateTimeOffset? nowOverride = null,
        CancellationToken cancellationToken = default)
    {
        var now = nowOverride ?? DateTimeOffset.UtcNow;
        var cfg = await configProvider.GetEffectiveConfigAsync(cancellationToken);
        var balanceResult = await smsProvider.GetBalanceAsync(cancellationToken);

        if (!balanceResult.Succeeded || !balanceResult.Balance.HasValue)
        {
            return new LowBalanceCheckResultDto(
                Checked: false,
                IsLow: false,
                AlertEmailSent: false,
                Balance: null,
                Threshold: cfg.LowBalanceThreshold,
                Reason: balanceResult.ErrorMessage ?? "Unable to check balance.");
        }

        var balance = balanceResult.Balance.Value;
        var isLow = balance < cfg.LowBalanceThreshold;
        var alertSent = false;

        using (tenantContext.BeginSystemScope())
        {
            var setting = await db.PlatformSmsSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => !s.IsDeleted && s.Key == "default", cancellationToken);

            if (setting is null)
            {
                setting = new PlatformSmsSetting
                {
                    Key = "default",
                    LowBalanceThreshold = cfg.LowBalanceThreshold
                };
                db.PlatformSmsSettings.Add(setting);
            }

            setting.LastKnownBalance = balance;
            setting.LastGatewayActive = balanceResult.IsActive;
            setting.BalanceCheckedAt = now;

            if (isLow)
            {
                var colomboTz = ResolveColomboTimeZone();
                var todayColombo = TimeZoneInfo.ConvertTime(now, colomboTz).Date;
                var alreadySentToday = setting.LastLowBalanceAlertSentAt.HasValue
                    && TimeZoneInfo.ConvertTime(setting.LastLowBalanceAlertSentAt.Value, colomboTz).Date == todayColombo;

                if (!alreadySentToday)
                {
                    var recipients = await ResolveSuperAdminEmailsAsync(cancellationToken);
                    if (recipients.Count > 0)
                    {
                        var subject = $"[{ProductName.Value}] Low SMS Gateway Balance Alert ({balance:N2} {balanceResult.Currency})";
                        var plain = $"Your {balanceResult.Provider} SMS gateway balance is {balance:N2} {balanceResult.Currency}, which is below your threshold of {cfg.LowBalanceThreshold:N2} {balanceResult.Currency}. Please top up your account to avoid SMS delivery interruptions.";
                        var html = $"<p>{WebUtility.HtmlEncode(plain)}</p>";

                        foreach (var email in recipients)
                        {
                            await emailSender.SendAsync(
                                new EmailMessage(
                                    To: email,
                                    Subject: subject,
                                    HtmlBody: html,
                                    PlainTextBody: plain,
                                    Metadata: new Dictionary<string, string>
                                    {
                                        ["Type"] = "SmsLowBalanceAlert",
                                        ["Provider"] = balanceResult.Provider
                                    }),
                                cancellationToken);
                        }

                        setting.LastLowBalanceAlertSentAt = now;
                        alertSent = true;
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return new LowBalanceCheckResultDto(
            Checked: true,
            IsLow: isLow,
            AlertEmailSent: alertSent,
            Balance: balance,
            Threshold: cfg.LowBalanceThreshold,
            Reason: isLow ? (alertSent ? "Alert sent" : "Already alerted today") : "Balance healthy");
    }

    public async Task<bool> IsPromotionalSenderIdConfiguredAsync(CancellationToken cancellationToken = default)
    {
        var cfg = await configProvider.GetEffectiveConfigAsync(cancellationToken);
        return !string.IsNullOrWhiteSpace(cfg.PromotionalSenderId);
    }

    private async Task<List<string>> ResolveSuperAdminEmailsAsync(CancellationToken cancellationToken)
    {
        var emails = await db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => !u.IsDeleted && u.IsActive && u.RoleType == RoleType.SuperAdmin && !string.IsNullOrWhiteSpace(u.Email))
            .Select(u => u.Email)
            .Distinct()
            .ToListAsync(cancellationToken);

        var seedEmail = configuration["Seed:SuperAdmin:Email"] ?? configuration["SUPERADMIN_EMAIL"];
        if (!string.IsNullOrWhiteSpace(seedEmail)
            && !emails.Contains(seedEmail.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            emails.Add(seedEmail.Trim());
        }

        return emails;
    }

    private static TimeZoneInfo ResolveColomboTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Sri Lanka Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.CreateCustomTimeZone("Asia/Colombo", TimeSpan.FromHours(5.5), "Asia/Colombo", "Asia/Colombo");
            }
        }
    }
}
