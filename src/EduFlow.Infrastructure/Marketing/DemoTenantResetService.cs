using EduFlow.Application.Common.Interfaces;
using EduFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Infrastructure.Marketing;

public interface IDemoTenantResetService
{
    Task ResetAsync(CancellationToken cancellationToken = default);
}

public sealed class DemoTenantResetService(
    AppDbContext db,
    DbSeeder seeder,
    ITenantContext tenantContext) : IDemoTenantResetService
{
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        using (tenantContext.BeginSystemScope())
        {
            var demoId = await db.Tenants
                .IgnoreQueryFilters()
                .Where(t => !t.IsDeleted && t.IsDemo && t.Subdomain == "demo")
                .Select(t => (Guid?)t.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (demoId is null)
            {
                return;
            }

            var id = demoId.Value;
            await db.AttendanceMarks.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.ClassSessions.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.ExamMarks.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.ExamResults.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.ExamSubjects.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.Exams.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.GradeBoundaries.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.GradingSchemes.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.FeePayments.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.PayHereCheckoutSessions.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.FeeInvoices.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.StudentConcessions.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.FeeStructures.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.NotificationOutboxes.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.ParentStudentLinks.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.StudentEnrollments.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.Students.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.ClassSchedules.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.Subjects.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.AcademicBatches.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
            await db.BatchGroups.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(cancellationToken);
        }

        db.ChangeTracker.Clear();
        await seeder.SeedAsync(cancellationToken);
    }
}
