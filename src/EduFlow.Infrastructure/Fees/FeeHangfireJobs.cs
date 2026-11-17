using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Fees;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduFlow.Infrastructure.Fees;

public sealed class MonthlyFeeInvoiceHangfireJob(
    IAppDbContext db,
    IFeeService feeService,
    ILogger<MonthlyFeeInvoiceHangfireJob> logger)
{
    public const string RecurringJobId = "monthly-fee-invoice-generation";

    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(string? billingPeriod = null, CancellationToken cancellationToken = default)
    {
        var activeTenantIds = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => !t.IsDeleted && t.IsActive)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        foreach (var tenantId in activeTenantIds)
        {
            var result = await feeService.GenerateMonthlyInvoicesAsync(
                new GenerateMonthlyInvoicesRequest(BillingPeriod: billingPeriod),
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);

            if (result.CreatedCount > 0)
            {
                logger.LogInformation(
                    "Generated {CreatedCount} monthly fee invoices for tenant {TenantId} ({BillingPeriod}).",
                    result.CreatedCount,
                    tenantId,
                    result.BillingPeriod);
            }
        }
    }
}

public sealed class FeeReminderHangfireJob(
    IAppDbContext db,
    IFeeService feeService,
    ILogger<FeeReminderHangfireJob> logger)
{
    public const string RecurringJobId = "fee-due-reminders-dispatch";

    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(DateOnly? dateOverride = null, CancellationToken cancellationToken = default)
    {
        var activeTenantIds = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => !t.IsDeleted && t.IsActive)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        foreach (var tenantId in activeTenantIds)
        {
            var result = await feeService.ProcessFeeRemindersAsync(
                dateOverride: dateOverride,
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);

            var totalSent = result.BeforeDueSent + result.OnDueSent + result.OverdueSent;
            if (totalSent > 0)
            {
                logger.LogInformation(
                    "Processed fee reminders for tenant {TenantId}: {BeforeDue} before-due, {OnDue} on-due, {Overdue} overdue.",
                    tenantId,
                    result.BeforeDueSent,
                    result.OnDueSent,
                    result.OverdueSent);
            }
        }
    }
}
