using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Exceptions;

public abstract class AppException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed class CrossTenantOperationException(string message = "Cross-tenant data access or modification is forbidden.")
    : AppException("CROSS_TENANT_ACCESS", message, 403);

public sealed class FeatureDisabledException(FeatureKey feature)
    : AppException("FEATURE_DISABLED", $"Feature '{feature}' is disabled for this institute.", 403)
{
    public string Feature { get; } = feature.ToString();
}

public sealed class FeatureNotInPlanException(FeatureKey feature)
    : AppException("FEATURE_NOT_IN_PLAN", $"Feature '{feature}' is not included in the current subscription plan.", 403)
{
    public string Feature { get; } = feature.ToString();
}

public sealed class PlanLimitReachedException(LimitKey limit, int max)
    : AppException("LIMIT_REACHED", $"The plan limit for '{limit}' ({max}) has been reached.", 403)
{
    public string Limit { get; } = limit.ToString();
    public int Max { get; } = max;
}

public sealed class SubscriptionBlockedException(string code, string message)
    : AppException(code, message, 403);

public sealed class ReadOnlyAccessException(string code = "IMPERSONATION_READ_ONLY", string message = "Read-only session cannot perform write operations.")
    : AppException(code, message, 403);

public sealed class AccountLockedException(DateTimeOffset lockoutEnd)
    : AppException("ACCOUNT_LOCKED", "Account is temporarily locked due to repeated failed login attempts.", 423)
{
    public DateTimeOffset LockoutEnd { get; } = lockoutEnd;
}

public sealed class DomainValidationException(string code, string message, int statusCode = 400)
    : AppException(code, message, statusCode);

public sealed class EntityNotFoundException(string entity, string? id = null)
    : AppException("NOT_FOUND", id is null ? $"{entity} was not found." : $"{entity} '{id}' was not found.", 404);
