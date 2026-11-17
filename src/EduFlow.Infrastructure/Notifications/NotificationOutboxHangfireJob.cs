using EduFlow.Application.Notifications;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace EduFlow.Infrastructure.Notifications;

public sealed class NotificationOutboxHangfireJob(
    INotificationDispatcher dispatcher,
    ILogger<NotificationOutboxHangfireJob> logger)
{
    public const string RecurringJobId = "notification-outbox-dispatch";

    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var result = await dispatcher.DispatchPendingAsync(cancellationToken: cancellationToken);
        if (result.ProcessedCount > 0)
        {
            logger.LogInformation(
                "Notification outbox job processed {Processed}: {Sent} sent, {Skipped} skipped, {Failed} failed, {Deferred} deferred, {DeadLetter} dead-lettered.",
                result.ProcessedCount,
                result.SentCount,
                result.SkippedCount,
                result.FailedCount,
                result.DeferredCount,
                result.DeadLetterCount);
        }
    }
}
