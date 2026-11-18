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
        bool isVoided = false,
        decimal creditAppliedAmount = 0m)
    {
        if (isVoided)
        {
            return (0m, 0m, FeeInvoiceStatus.Voided);
        }

        var net = RoundMoney(Math.Max(0m, netAmount));
        var credit = Math.Clamp(RoundMoney(Math.Max(0m, creditAppliedAmount)), 0m, net);
        var paid = RoundMoney(payments.Sum(p => EffectivePaymentAmount(p.Amount, p.RefundedAmount, p.Status)));
        var balance = RoundMoney(Math.Max(0m, net - credit - paid));

        var status = net == 0m || RoundMoney(credit + paid) >= net
            ? FeeInvoiceStatus.Paid
            : RoundMoney(credit + paid) > 0m
                ? FeeInvoiceStatus.PartiallyPaid
                : FeeInvoiceStatus.Pending;

        return (paid, balance, status);
    }

    public static int GetSiblingRank(
        Guid targetStudentId,
        IEnumerable<(Guid Id, DateOnly AdmissionDate, DateTimeOffset CreatedAt, string StudentCode)> siblingGroup)
    {
        var ordered = siblingGroup
            .GroupBy(x => x.Id)
            .Select(g => g.First())
            .OrderBy(x => x.AdmissionDate)
            .ThenBy(x => x.CreatedAt)
            .ThenBy(x => x.StudentCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id)
            .ToList();

        var index = ordered.FindIndex(x => x.Id == targetStudentId);
        return index >= 0 ? index + 1 : 1;
    }

    public static (decimal DiscountAmount, decimal NetAmount, bool UsedConcession, string? Description) CalculateCombinedInvoiceDiscount(
        decimal grossAmount,
        DiscountType? concessionType,
        decimal? concessionValue,
        string? concessionName,
        DiscountType? siblingType,
        decimal? siblingValue,
        int siblingRank,
        bool stackWithConcession,
        string currency = "LKR")
    {
        var gross = RoundMoney(Math.Max(0m, grossAmount));
        var concessionDiscount = concessionType.HasValue && concessionValue.HasValue && concessionValue.Value > 0m
            ? CalculateDiscount(gross, concessionType.Value, concessionValue.Value)
            : 0m;
        var hasConcession = concessionDiscount > 0m;

        var siblingDiscountRaw = siblingRank >= 2 && siblingType.HasValue && siblingValue.HasValue && siblingValue.Value > 0m
            ? CalculateDiscount(gross, siblingType.Value, siblingValue.Value)
            : 0m;

        if (!hasConcession && siblingDiscountRaw <= 0m)
        {
            return (0m, gross, false, null);
        }

        var concessionText = hasConcession
            ? $"{concessionName} ({FormatDiscountValue(concessionType!.Value, concessionValue!.Value, currency)})"
            : null;

        var rankLabel = siblingRank == 2 ? "2nd child" : $"{siblingRank}th child";
        var siblingText = siblingDiscountRaw > 0m
            ? $"Sibling Discount ({rankLabel}: {FormatDiscountValue(siblingType!.Value, siblingValue!.Value, currency)})"
            : null;

        if (!stackWithConcession)
        {
            if (concessionDiscount >= siblingDiscountRaw && hasConcession)
            {
                return (concessionDiscount, RoundMoney(Math.Max(0m, gross - concessionDiscount)), true, concessionText);
            }

            if (siblingDiscountRaw > 0m)
            {
                return (siblingDiscountRaw, RoundMoney(Math.Max(0m, gross - siblingDiscountRaw)), false, siblingText);
            }
        }

        // Stacked: apply individual concession first, then sibling discount on the remaining amount.
        var afterConcession = RoundMoney(Math.Max(0m, gross - concessionDiscount));
        var stackedSiblingDiscount = siblingRank >= 2 && siblingType.HasValue && siblingValue.HasValue && siblingValue.Value > 0m
            ? CalculateDiscount(afterConcession, siblingType.Value, siblingValue.Value)
            : 0m;

        var totalDiscount = Math.Clamp(RoundMoney(concessionDiscount + stackedSiblingDiscount), 0m, gross);
        var net = RoundMoney(Math.Max(0m, gross - totalDiscount));
        var parts = new[] { concessionText, stackedSiblingDiscount > 0m ? siblingText : null }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();

        return (totalDiscount, net, hasConcession, parts.Length > 0 ? string.Join(" + ", parts) : null);
    }

    public static (decimal AppliedAmount, decimal RemainingCredit) CalculateCreditApplication(
        decimal availableCredit,
        decimal invoiceBalance)
    {
        var credit = RoundMoney(Math.Max(0m, availableCredit));
        var balance = RoundMoney(Math.Max(0m, invoiceBalance));
        if (credit <= 0m || balance <= 0m)
        {
            return (0m, credit);
        }

        var applied = RoundMoney(Math.Min(credit, balance));
        var remaining = RoundMoney(Math.Max(0m, credit - applied));
        return (applied, remaining);
    }

    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) GetColomboDayWindowUtc(DateOnly date)
    {
        var colomboOffset = TimeSpan.FromMinutes(330);
        var startLocal = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), colomboOffset);
        var startUtc = startLocal.ToUniversalTime();
        return (startUtc, startUtc.AddDays(1));
    }

    private static string FormatDiscountValue(DiscountType type, decimal value, string currency) =>
        type == DiscountType.Percentage
            ? $"{value:0.##}%"
            : FormatCurrency(value, currency);

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
