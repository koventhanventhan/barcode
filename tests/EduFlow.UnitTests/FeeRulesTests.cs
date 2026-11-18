using EduFlow.Application.Fees;
using EduFlow.Domain.Enums;
using EduFlow.Infrastructure.Payments;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;

namespace EduFlow.UnitTests;

public sealed class FeeRulesTests
{
    [Fact]
    public void Concession_and_partial_payments_decimal_math_is_exact()
    {
        var (pctDiscount, pctNet) = FeeRules.CalculateInvoiceAmounts(3500.00m, DiscountType.Percentage, 12.5m);
        pctDiscount.Should().Be(437.50m);
        pctNet.Should().Be(3062.50m);

        var (fixedDiscount, fixedNet) = FeeRules.CalculateInvoiceAmounts(3500.00m, DiscountType.FixedAmount, 750.25m);
        fixedDiscount.Should().Be(750.25m);
        fixedNet.Should().Be(2749.75m);

        // Partial payments summing up with decimal precision
        var afterFirst = FeeRules.ComputeInvoiceSettlement(
            3062.50m,
            [(1020.83m, 0m, PaymentRecordStatus.Completed)]);
        afterFirst.PaidAmount.Should().Be(1020.83m);
        afterFirst.BalanceAmount.Should().Be(2041.67m);
        afterFirst.Status.Should().Be(FeeInvoiceStatus.PartiallyPaid);

        var afterSecond = FeeRules.ComputeInvoiceSettlement(
            3062.50m,
            [
                (1020.83m, 0m, PaymentRecordStatus.Completed),
                (2041.67m, 0m, PaymentRecordStatus.Completed)
            ]);
        afterSecond.PaidAmount.Should().Be(3062.50m);
        afterSecond.BalanceAmount.Should().Be(0.00m);
        afterSecond.Status.Should().Be(FeeInvoiceStatus.Paid);

        // Partial refund re-opens balance
        var afterPartialRefund = FeeRules.ComputeInvoiceSettlement(
            3062.50m,
            [
                (1020.83m, 220.83m, PaymentRecordStatus.Refunded),
                (2041.67m, 0m, PaymentRecordStatus.Completed)
            ]);
        afterPartialRefund.PaidAmount.Should().Be(2841.67m);
        afterPartialRefund.BalanceAmount.Should().Be(220.83m);
        afterPartialRefund.Status.Should().Be(FeeInvoiceStatus.PartiallyPaid);

        // Voided payment contributes 0
        var afterVoid = FeeRules.ComputeInvoiceSettlement(
            3062.50m,
            [
                (1020.83m, 1020.83m, PaymentRecordStatus.Voided),
                (2041.67m, 2041.67m, PaymentRecordStatus.Voided)
            ]);
        afterVoid.PaidAmount.Should().Be(0.00m);
        afterVoid.BalanceAmount.Should().Be(3062.50m);
        afterVoid.Status.Should().Be(FeeInvoiceStatus.Pending);
    }

    [Fact]
    public void Reminder_schedule_evaluates_before_due_on_due_and_overdue_stages()
    {
        var dueDate = new DateOnly(2026, 11, 15);

        // 3 days before due date (2026-11-12)
        FeeRules.EvaluateReminderStage(
            new DateOnly(2026, 11, 12), dueDate, 3, true, 3,
            beforeSent: false, onDueSent: false, overdueSent: false)
            .Should().Be(FeeReminderStage.BeforeDue);

        // Already sent before-due -> null on same day
        FeeRules.EvaluateReminderStage(
            new DateOnly(2026, 11, 12), dueDate, 3, true, 3,
            beforeSent: true, onDueSent: false, overdueSent: false)
            .Should().BeNull();

        // On due date (2026-11-15)
        FeeRules.EvaluateReminderStage(
            new DateOnly(2026, 11, 15), dueDate, 3, true, 3,
            beforeSent: true, onDueSent: false, overdueSent: false)
            .Should().Be(FeeReminderStage.OnDueDate);

        // 2 days after due date (not yet 3 days overdue) -> null
        FeeRules.EvaluateReminderStage(
            new DateOnly(2026, 11, 17), dueDate, 3, true, 3,
            beforeSent: true, onDueSent: true, overdueSent: false)
            .Should().BeNull();

        // 3 days after due date (2026-11-18) -> Overdue
        FeeRules.EvaluateReminderStage(
            new DateOnly(2026, 11, 18), dueDate, 3, true, 3,
            beforeSent: true, onDueSent: true, overdueSent: false)
            .Should().Be(FeeReminderStage.Overdue);
    }

    [Fact]
    public void PayHere_gateway_encrypts_secrets_and_verifies_md5sig_accurately()
    {
        var provider = DataProtectionProvider.Create("Admino.Tests");
        var gateway = new PayHerePaymentGateway(provider);

        const string merchantId = "1211149";
        const string plainSecret = "MzI0MTU5MjY1MzU4OTc5MzIzODQ2";
        const string orderId = "PH-INV000001-ABCD1234";

        var encrypted = gateway.ProtectSecret(plainSecret);
        encrypted.Should().NotBe(plainSecret);
        gateway.UnprotectSecret(encrypted).Should().Be(plainSecret);
        gateway.MaskSecret(plainSecret).Should().EndWith("ODQ2");

        var checkoutHash = gateway.GenerateCheckoutHash(merchantId, plainSecret, orderId, 3500.00m, "LKR");
        checkoutHash.Should().HaveLength(32);

        var validSig = gateway.GenerateWebhookSignature(merchantId, plainSecret, orderId, "3500.00", "LKR", 2);
        gateway.VerifyWebhookSignature(merchantId, plainSecret, orderId, "3500.00", "LKR", 2, validSig).Should().BeTrue();
        gateway.VerifyWebhookSignature(merchantId, plainSecret, orderId, "3500.00", "LKR", 2, validSig.ToLowerInvariant()).Should().BeTrue();

        // Tampered amount or signature must fail verification
        gateway.VerifyWebhookSignature(merchantId, plainSecret, orderId, "100.00", "LKR", 2, validSig).Should().BeFalse();
        gateway.VerifyWebhookSignature(merchantId, plainSecret, orderId, "3500.00", "LKR", 2, "00000000000000000000000000000000").Should().BeFalse();
    }

    [Fact]
    public void Credit_balance_application_and_settlement_decimal_math_is_exact()
    {
        var (applied1, remaining1) = FeeRules.CalculateCreditApplication(5000.50m, 3500.25m);
        applied1.Should().Be(3500.25m);
        remaining1.Should().Be(1500.25m);

        var (applied2, remaining2) = FeeRules.CalculateCreditApplication(remaining1, 4000.00m);
        applied2.Should().Be(1500.25m);
        remaining2.Should().Be(0.00m);

        var partial = FeeRules.ComputeInvoiceSettlement(
            4000.00m,
            [],
            isVoided: false,
            creditAppliedAmount: applied2);
        partial.PaidAmount.Should().Be(0.00m);
        partial.BalanceAmount.Should().Be(2499.75m);
        partial.Status.Should().Be(FeeInvoiceStatus.PartiallyPaid);

        var paid = FeeRules.ComputeInvoiceSettlement(
            4000.00m,
            [(2499.75m, 0m, PaymentRecordStatus.Completed)],
            isVoided: false,
            creditAppliedAmount: applied2);
        paid.PaidAmount.Should().Be(2499.75m);
        paid.BalanceAmount.Should().Be(0.00m);
        paid.Status.Should().Be(FeeInvoiceStatus.Paid);
    }

    [Fact]
    public void Sibling_rank_and_combined_discount_math_handles_non_stacked_and_stacked_rules()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var group = new[]
        {
            (id3, new DateOnly(2026, 3, 1), now.AddDays(2), "S000003"),
            (id1, new DateOnly(2025, 1, 10), now, "S000001"),
            (id2, new DateOnly(2026, 1, 15), now.AddDays(1), "S000002")
        };

        FeeRules.GetSiblingRank(id1, group).Should().Be(1);
        FeeRules.GetSiblingRank(id2, group).Should().Be(2);
        FeeRules.GetSiblingRank(id3, group).Should().Be(3);

        var best = FeeRules.CalculateCombinedInvoiceDiscount(
            4000.00m,
            DiscountType.Percentage, 15m, "Merit",
            DiscountType.Percentage, 10m, siblingRank: 2,
            stackWithConcession: false);
        best.DiscountAmount.Should().Be(600.00m);
        best.NetAmount.Should().Be(3400.00m);
        best.UsedConcession.Should().BeTrue();

        var stacked = FeeRules.CalculateCombinedInvoiceDiscount(
            4000.00m,
            DiscountType.Percentage, 50m, "Half Scholarship",
            DiscountType.Percentage, 20m, siblingRank: 3,
            stackWithConcession: true);
        stacked.DiscountAmount.Should().Be(2400.00m);
        stacked.NetAmount.Should().Be(1600.00m);
        stacked.UsedConcession.Should().BeTrue();
        stacked.Description.Should().Contain("Sibling Discount");
    }

    [Fact]
    public void Colombo_day_window_uses_utc_plus_0530_boundaries()
    {
        var (startUtc, endUtc) = FeeRules.GetColomboDayWindowUtc(new DateOnly(2026, 11, 18));
        startUtc.UtcDateTime.Should().Be(new DateTime(2026, 11, 17, 18, 30, 0, DateTimeKind.Utc));
        endUtc.UtcDateTime.Should().Be(new DateTime(2026, 11, 18, 18, 30, 0, DateTimeKind.Utc));
    }
}
