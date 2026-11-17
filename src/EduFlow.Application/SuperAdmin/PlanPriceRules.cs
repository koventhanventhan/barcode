using EduFlow.Domain.Exceptions;

namespace EduFlow.Application.SuperAdmin;

public static class PlanPriceRules
{
    public const decimal MaxLkr = 100_000_000m;

    public static void EnsureValid(
        decimal? priceMonthlyLkr,
        decimal? priceYearlyLkr,
        decimal? setupFeeLkr,
        decimal? extraSmsPerSegmentLkr,
        decimal? extraStoragePerGbLkr)
    {
        EnsureMoney(priceMonthlyLkr, "PriceMonthlyLkr");
        EnsureMoney(priceYearlyLkr, "PriceYearlyLkr");
        EnsureMoney(setupFeeLkr, "SetupFeeLkr");
        EnsureMoney(extraSmsPerSegmentLkr, "ExtraSmsPerSegmentLkr");
        EnsureMoney(extraStoragePerGbLkr, "ExtraStoragePerGbLkr");
    }

    private static void EnsureMoney(decimal? value, string field)
    {
        if (value is null)
        {
            return;
        }

        if (value < 0 || value > MaxLkr || decimal.Round(value.Value, 2) != value.Value)
        {
            throw new DomainValidationException(
                "INVALID_PLAN_PRICE",
                $"{field} must be blank or an LKR amount from 0 to {MaxLkr:0} with at most 2 decimal places.");
        }
    }
}
