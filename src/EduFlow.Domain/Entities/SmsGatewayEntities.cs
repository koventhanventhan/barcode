using EduFlow.Domain.Common;

namespace EduFlow.Domain.Entities;

public sealed class PlatformSmsSetting : BaseEntity
{
    public string Key { get; set; } = "default";
    public string? Provider { get; set; }
    public string? UserId { get; set; }
    public string? EncryptedApiKey { get; set; }
    public string? SenderId { get; set; }
    public string? PromotionalSenderId { get; set; }
    public string? BaseUrl { get; set; }
    public bool? TestMode { get; set; }
    public string? TestAllowList { get; set; }
    public decimal LowBalanceThreshold { get; set; } = 500m;
    public DateTimeOffset? LastLowBalanceAlertSentAt { get; set; }
    public decimal? LastKnownBalance { get; set; }
    public bool? LastGatewayActive { get; set; }
    public DateTimeOffset? BalanceCheckedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

public sealed class SmsGatewayErrorLog : BaseEntity
{
    public Guid? TenantId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string? Recipient { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
