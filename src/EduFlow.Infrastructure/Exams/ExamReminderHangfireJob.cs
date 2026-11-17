using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Exams;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduFlow.Infrastructure.Exams;

public sealed class ExamReminderHangfireJob(
    IAppDbContext db,
    IExamService examService,
    ILogger<ExamReminderHangfireJob> logger)
{
    public const string RecurringJobId = "exam-reminder-dispatch";

    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(DateTimeOffset? nowOverride = null, CancellationToken cancellationToken = default)
    {
        var activeTenantIds = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => !t.IsDeleted && t.IsActive)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        foreach (var tenantId in activeTenantIds)
        {
            var result = await examService.ProcessExamRemindersAsync(nowOverride, tenantId, cancellationToken);
            if (result.MessagesEnqueued > 0)
            {
                logger.LogInformation(
                    "Sent exam reminders for tenant {TenantId}: {ExamsReminded} exams, {MessagesEnqueued} messages.",
                    tenantId,
                    result.ExamsReminded,
                    result.MessagesEnqueued);
            }
        }
    }
}
