namespace EduFlow.Application.Common.Interfaces;

public sealed record SmsSendRequest(
    string To,
    string Message,
    string? SenderId = null,
    Guid? TenantId = null,
    string? IdempotencyKey = null,
    bool IsPromotional = false);

public sealed record SmsSendResult(
    bool Succeeded,
    string Provider,
    string? ProviderMessageId = null,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    bool IsSkipped = false,
    string? SkipReason = null,
    int? GatewaySegments = null,
    decimal? GatewayCost = null);

public sealed record SmsGatewayBalanceResult(
    bool Succeeded,
    string Provider,
    bool? IsActive = null,
    decimal? Balance = null,
    string Currency = "LKR",
    string? ErrorCode = null,
    string? ErrorMessage = null);

public interface ISmsProvider
{
    string ProviderName { get; }

    Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default);

    Task<SmsGatewayBalanceResult> GetBalanceAsync(CancellationToken cancellationToken = default);
}

public sealed record SmsSegmentCalculation(
    string Encoding,
    bool IsUnicode,
    int CharacterCount,
    int Segments,
    int PerSegmentLimit,
    int RemainingInCurrentSegment);

public interface ISmsSegmentCalculator
{
    SmsSegmentCalculation Calculate(string? text);
}
