using EduFlow.Application.Notifications;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace EduFlow.Infrastructure.Notifications;

public sealed class LowBalanceAlertHangfireJob(
    ISmsGatewayAdminService smsGatewayAdmin,
    ILogger<LowBalanceAlertHangfireJob> logger)
{
    public const string RecurringJobId = "sms-low-balance-check";

    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var result = await smsGatewayAdmin.CheckLowBalanceAndNotifyAsync(cancellationToken: cancellationToken);
        if (result.IsLow)
        {
            logger.LogWarning(
                "SMS gateway balance {Balance} is below threshold {Threshold}. AlertEmailSent={AlertEmailSent} ({Reason})",
                result.Balance,
                result.Threshold,
                result.AlertEmailSent,
                result.Reason);
        }
    }
}
