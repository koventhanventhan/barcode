namespace EduFlow.Application.Notifications;

public sealed record EffectiveSmsGatewayConfig(
    string Provider,
    string UserId,
    string ApiKey,
    string SenderId,
    string? PromotionalSenderId,
    string BaseUrl,
    bool TestMode,
    IReadOnlyList<string> TestAllowList,
    decimal LowBalanceThreshold);

public sealed record SmsGatewayErrorDto(
    Guid Id,
    Guid? TenantId,
    string Provider,
    string Operation,
    string? Recipient,
    string ErrorCode,
    string ErrorMessage,
    DateTimeOffset OccurredAt);

public sealed record SuperAdminSmsSettingsDto(
    string Provider,
    string UserId,
    bool HasApiKey,
    string ApiKeySource,
    string SenderId,
    string? PromotionalSenderId,
    string BaseUrl,
    bool TestMode,
    IReadOnlyList<string> TestAllowList,
    decimal LowBalanceThreshold,
    DateTimeOffset? LastLowBalanceAlertSentAt,
    IReadOnlyList<SmsGatewayErrorDto> RecentErrors);

public sealed record UpdateSuperAdminSmsSettingsRequest(
    string Provider,
    string UserId,
    string? ApiKey,
    string SenderId,
    string? PromotionalSenderId,
    string BaseUrl,
    bool TestMode,
    string? TestAllowList,
    decimal LowBalanceThreshold,
    bool ClearApiKey = false);

public sealed record SmsGatewayBalanceDto(
    bool Succeeded,
    string Provider,
    bool? IsActive,
    decimal? Balance,
    string Currency,
    bool IsBelowThreshold,
    decimal LowBalanceThreshold,
    DateTimeOffset CheckedAt,
    bool FromCache,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record SendSuperAdminTestSmsRequest(
    string To,
    string Message,
    string? SenderId = null,
    bool IsPromotional = false);

public sealed record SuperAdminTestSmsResultDto(
    bool Succeeded,
    bool IsSkipped,
    string? SkipReason,
    string Provider,
    string? ProviderMessageId,
    string? ErrorCode,
    string? ErrorMessage,
    int Segments,
    int? GatewaySegments,
    decimal? GatewayCost);

public sealed record LowBalanceCheckResultDto(
    bool Checked,
    bool IsLow,
    bool AlertEmailSent,
    decimal? Balance,
    decimal Threshold,
    string? Reason);

public interface ISmsGatewayConfigProvider
{
    Task<EffectiveSmsGatewayConfig> GetEffectiveConfigAsync(CancellationToken cancellationToken = default);
    Task<string?> ResolveTenantSenderIdAsync(Guid? tenantId, bool isPromotional, string? explicitSenderId, CancellationToken cancellationToken = default);
    Task RecordErrorAsync(string provider, string operation, string? recipient, string errorCode, string errorMessage, Guid? tenantId = null, CancellationToken cancellationToken = default);
}

public interface ISmsGatewayAdminService
{
    Task<SuperAdminSmsSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<SuperAdminSmsSettingsDto> UpdateSettingsAsync(UpdateSuperAdminSmsSettingsRequest request, CancellationToken cancellationToken = default);
    Task<SmsGatewayBalanceDto> GetBalanceAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    Task<SuperAdminTestSmsResultDto> SendTestSmsAsync(SendSuperAdminTestSmsRequest request, CancellationToken cancellationToken = default);
    Task<LowBalanceCheckResultDto> CheckLowBalanceAndNotifyAsync(DateTimeOffset? nowOverride = null, CancellationToken cancellationToken = default);
    Task<bool> IsPromotionalSenderIdConfiguredAsync(CancellationToken cancellationToken = default);
}
