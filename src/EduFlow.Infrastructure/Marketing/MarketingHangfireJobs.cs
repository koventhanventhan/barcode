using EduFlow.Application.Marketing;

namespace EduFlow.Infrastructure.Marketing;

public sealed class TrialLifecycleHangfireJob(ITrialNoticeService trialNotices)
{
    public const string RecurringJobId = "trial-lifecycle-notices";

    public Task ExecuteAsync(CancellationToken cancellationToken = default) =>
        trialNotices.SendDueNoticesAsync(null, cancellationToken);
}

public sealed class DemoTenantResetHangfireJob(IDemoTenantResetService resetService)
{
    public const string RecurringJobId = "demo-tenant-nightly-reset";

    public Task ExecuteAsync(CancellationToken cancellationToken = default) =>
        resetService.ResetAsync(cancellationToken);
}
