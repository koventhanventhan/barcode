using System.Globalization;
using System.Text.RegularExpressions;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;

namespace EduFlow.Application.Fees;

public static partial class FeeRules
{
    public static readonly string[] AgingBuckets = ["Current", "1-30 Days", "31-60 Days", "61-90 Days", "90+ Days"];

    [GeneratedRegex(@"^(?<year>20\d{2})-(?<month>0[1-9]|1[0-2])$")]
    private static partial Regex MonthlyPeriodRegex();

    public static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal CalculateDiscount(decimal grossAmount, DiscountType discountType, decimal value)
    {
        var gross = RoundMoney(Math.Max(0m, grossAmount));
        if (gross <= 0m || value <= 0m)
        {
            return 0m;
        }

        var rawDiscount = discountType switch
        {
            DiscountType.Percentage => RoundMoney(gross * (Math.Min(100m, value) / 100m)),
            DiscountType.FixedAmount => RoundMoney(value),
            _ => 0m
        };

        return Math.Clamp(rawDiscount, 0m, gross);
    }

    public static (decimal DiscountAmount, decimal NetAmount) CalculateInvoiceAmounts(
        decimal grossAmount,
        DiscountType? discountType = null,
        decimal? discountValue = null)
    {
        var gross = RoundMoney(Math.Max(0m, grossAmount));
        var discount = discountType.HasValue && discountValue.HasValue
            ? CalculateDiscount(gross, discountType.Value, discountValue.Value)
            : 0m;
        var net = RoundMoney(Math.Max(0m, gross - discount));
        return (discount, net);
    }

    public static decimal EffectivePaymentAmount(decimal amount, decimal refundedAmount, PaymentRecordStatus status)
    {
        if (status == PaymentRecordStatus.Voided)
        {
            return 0m;
        }

        return RoundMoney(Math.Max(0m, amount - refundedAmount));
    }

    public static (decimal PaidAmount, decimal BalanceAmount, FeeInvoiceStatus Status) ComputeInvoiceSettlement(
        decimal netAmount,
        IEnumerable<(decimal Amount, decimal RefundedAmount, PaymentRecordStatus Status)> payments,
        bool isVoided = false)
    {
        if (isVoided)
        {
            return (0m, 0m, FeeInvoiceStatus.Voided);
        }

        var net = RoundMoney(Math.Max(0m, netAmount));
        var paid = RoundMoney(payments.Sum(p => EffectivePaymentAmount(p.Amount, p.RefundedAmount, p.Status)));
        var balance = RoundMoney(Math.Max(0m, net - paid));

        var status = net == 0m || paid >= net
            ? FeeInvoiceStatus.Paid
            : paid > 0m
                ? FeeInvoiceStatus.PartiallyPaid
                : FeeInvoiceStatus.Pending;

        return (paid, balance, status);
    }

    public static string NormalizeMonthlyPeriod(string? billingPeriod, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(billingPeriod))
        {
            return $"{today.Year:D4}-{today.Month:D2}";
        }

        var trimmed = billingPeriod.Trim();
        var match = MonthlyPeriodRegex().Match(trimmed);
        if (!match.Success)
        {
            throw new DomainValidationException(
                "INVALID_BILLING_PERIOD",
                "Billing period must be in YYYY-MM format (for example 2026-11).");
        }

        return trimmed;
    }

    public static string ResolvePeriodForStructure(FeeFrequency frequency, string? requestedPeriod, DateOnly today)
    {
        return frequency switch
        {
            FeeFrequency.Monthly => NormalizeMonthlyPeriod(requestedPeriod, today),
            FeeFrequency.Admission => string.IsNullOrWhiteSpace(requestedPeriod) ? "ADMISSION" : requestedPeriod.Trim().ToUpperInvariant(),
            FeeFrequency.OneTime => string.IsNullOrWhiteSpace(requestedPeriod) ? "ONETIME" : requestedPeriod.Trim().ToUpperInvariant(),
            _ => NormalizeMonthlyPeriod(requestedPeriod, today)
        };
    }

    public static DateOnly ComputeDueDate(string billingPeriod, int dueDayOfMonth, DateOnly fallbackToday)
    {
        var safeDay = Math.Clamp(dueDayOfMonth, 1, 28);
        var match = MonthlyPeriodRegex().Match(billingPeriod);
        if (match.Success)
        {
            var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
            var maxDay = DateTime.DaysInMonth(year, month);
            return new DateOnly(year, month, Math.Min(safeDay, maxDay));
        }

        return fallbackToday.AddDays(7);
    }

    public static FeeReminderStage? EvaluateReminderStage(
        DateOnly today,
        DateOnly dueDate,
        int daysBefore,
        bool onDueDate,
        int daysAfterOverdue,
        bool beforeSent,
        bool onDueSent,
        bool overdueSent)
    {
        if (daysBefore > 0 && !beforeSent && today == dueDate.AddDays(-daysBefore))
        {
            return FeeReminderStage.BeforeDue;
        }

        if (onDueDate && !onDueSent && today == dueDate)
        {
            return FeeReminderStage.OnDueDate;
        }

        if (daysAfterOverdue > 0 && !overdueSent && today >= dueDate.AddDays(daysAfterOverdue))
        {
            return FeeReminderStage.Overdue;
        }

        return null;
    }

    public static string ClassifyAgingBucket(DateOnly today, DateOnly dueDate)
    {
        var daysOverdue = today.DayNumber - dueDate.DayNumber;
        return daysOverdue switch
        {
            <= 0 => "Current",
            <= 30 => "1-30 Days",
            <= 60 => "31-60 Days",
            <= 90 => "61-90 Days",
            _ => "90+ Days"
        };
    }

    public static int DaysOverdue(DateOnly today, DateOnly dueDate, decimal balanceAmount, FeeInvoiceStatus status)
    {
        if (status is FeeInvoiceStatus.Paid or FeeInvoiceStatus.Voided || balanceAmount <= 0m)
        {
            return 0;
        }

        return Math.Max(0, today.DayNumber - dueDate.DayNumber);
    }

    public static string FormatReceiptNumber(int sequence) => $"RCP-{Math.Max(1, sequence):D6}";

    public static string FormatInvoiceNumber(int sequence) => $"INV-{Math.Max(1, sequence):D6}";

    public static string FormatPlatformInvoiceNumber(int sequence) => $"PLAT-{Math.Max(1, sequence):D6}";

    public static int ParseSequence(string? formattedCode)
    {
        if (string.IsNullOrWhiteSpace(formattedCode))
        {
            return 0;
        }

        var dashIndex = formattedCode.LastIndexOf('-');
        var digits = dashIndex >= 0 ? formattedCode[(dashIndex + 1)..] : formattedCode;
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : 0;
    }

    public static string FormatCurrency(decimal amount, string currency = "LKR") =>
        $"{(string.IsNullOrWhiteSpace(currency) ? "LKR" : currency.Trim().ToUpperInvariant())} {RoundMoney(amount).ToString("N2", CultureInfo.InvariantCulture)}";
}
