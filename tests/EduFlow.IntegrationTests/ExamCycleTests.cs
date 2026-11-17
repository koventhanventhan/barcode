using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduFlow.Application.Academics;
using EduFlow.Application.Exams;
using EduFlow.Application.Notifications;
using EduFlow.Application.Students;
using EduFlow.Domain.Enums;
using EduFlow.Infrastructure.Exams;
using EduFlow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduFlow.IntegrationTests;

public sealed class ExamCycleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Publish_ranks_ties_once_and_prints_a_report_card()
    {
        var client = factory.CreateClient();
        var ctx = await SeedAsync(client, "exam-rank", "Pro");
        var schemeId = await DefaultSchemeIdAsync(client, ctx);

        var first = await CreateExamAsync(client, ctx, "Term 1", ctx.BatchA, schemeId, "term-1", ctx.Maths, ctx.Science);
        var second = await CreateExamAsync(client, ctx, "Term 1", ctx.BatchB, schemeId, "term-1", ctx.Maths, ctx.Science);
        var when = DateTimeOffset.UtcNow.AddHours(20);
        await ScheduleAsync(client, ctx, first.Id, when);
        await ScheduleAsync(client, ctx, second.Id, when);

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ExamReminderHangfireJob>();
            await job.ExecuteAsync(DateTimeOffset.UtcNow);
            await job.ExecuteAsync(DateTimeOffset.UtcNow);
        }

        (await CountMessagesAsync(ctx.Tenant.Id, NotificationMessageType.ExamReminder)).Should().Be(3);

        await SaveMarksAsync(client, ctx, first, ctx.Nimal, 90, 80, "Steady progress", null);
        await SaveMarksAsync(client, ctx, first, ctx.Amaya, 90, 80, null, "Keep the tie honest");
        await SaveMarksAsync(client, ctx, second, ctx.Kamal, 40, 20, null, null);

        using var lockReq = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Post, $"/api/exams/{first.Id}/lock", ctx.Auth.AccessToken, ctx.Tenant.Subdomain);
        (await client.SendAsync(lockReq)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var lockedSave = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            $"/api/exams/{first.Id}/marks",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain,
            new SaveMarksRequest([new MarkEntryRequest(ctx.Nimal, first.Subjects[0].Id, 10, false)]));
        (await client.SendAsync(lockedSave)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var unlockReq = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Post, $"/api/exams/{first.Id}/unlock", ctx.Auth.AccessToken, ctx.Tenant.Subdomain);
        (await client.SendAsync(unlockReq)).EnsureSuccessStatusCode();

        var published = await PublishAsync(client, ctx, first.Id);
        published.StudentsRanked.Should().Be(2);
        published.MessagesEnqueued.Should().Be(2);
        var publishedB = await PublishAsync(client, ctx, second.Id);
        publishedB.MessagesEnqueued.Should().Be(1);
        var again = await PublishAsync(client, ctx, first.Id);
        again.MessagesEnqueued.Should().Be(0);
        (await CountMessagesAsync(ctx.Tenant.Id, NotificationMessageType.MarksReleased)).Should().Be(3);

        var batchRanks = await RanksAsync(client, ctx, first.Id, "batch");
        batchRanks.Rows.Should().OnlyContain(r => r.Rank == 1 && r.Grade == "A");
        var levelRanks = await RanksAsync(client, ctx, first.Id, "level");
        levelRanks.Rows.Single(r => r.StudentId == ctx.Kamal).Rank.Should().Be(3);
        levelRanks.Rows.Single(r => r.StudentId == ctx.Kamal).Grade.Should().Be("F");
        levelRanks.Rows.Where(r => r.StudentId != ctx.Kamal).Should().OnlyContain(r => r.Rank == 1 && r.Grade == "A");

        var subjectRanks = await RanksAsync(client, ctx, first.Id, "subject", ctx.Maths);
        subjectRanks.Rows.Single(r => r.StudentId == ctx.Kamal).Rank.Should().Be(3);
        subjectRanks.Rows.Where(r => r.StudentId != ctx.Kamal).Should().OnlyContain(r => r.Rank == 1);

        var distribution = await SendAsync<List<GradeDistributionDto>>(client, ctx, HttpMethod.Get, $"/api/exams/{first.Id}/distribution");
        distribution.Single(d => d.Grade == "A").Count.Should().Be(2);
        distribution.Single(d => d.Grade == "F").Count.Should().Be(0);

        var trends = await SendAsync<List<StudentTrendPointDto>>(client, ctx, HttpMethod.Get, $"/api/exams/students/{ctx.Nimal}/trends");
        trends.Should().ContainSingle();
        trends[0].Percentage.Should().Be(85.00m);
        trends[0].BatchRank.Should().Be(1);
        trends[0].LevelRank.Should().Be(1);

        using var pdfReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/exams/{first.Id}/report-cards?studentId={ctx.Nimal}",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain);
        var pdfRes = await client.SendAsync(pdfReq);
        pdfRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var pdf = await pdfRes.Content.ReadAsByteArrayAsync();
        pdf.Length.Should().BeGreaterThan(500);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task Exam_notification_switch_off_skips_publish_messages_until_it_is_on()
    {
        var client = factory.CreateClient();
        var ctx = await SeedAsync(client, "exam-off", "Pro");
        var schemeId = await DefaultSchemeIdAsync(client, ctx);
        var exam = await CreateExamAsync(client, ctx, "Unit test", ctx.BatchA, schemeId, "unit-off", ctx.Maths, ctx.Science);
        using var offReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/settings/features/ExamNotifications",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain,
            new EduFlow.Api.Endpoints.UpdateFeatureSwitchRequest(false));
        (await client.SendAsync(offReq)).EnsureSuccessStatusCode();

        await ScheduleAsync(client, ctx, exam.Id, DateTimeOffset.UtcNow.AddDays(3));
        await SaveMarksAsync(client, ctx, exam, ctx.Nimal, 70, 70, null, null);
        await SaveMarksAsync(client, ctx, exam, ctx.Amaya, 60, 60, null, null);

        var blocked = await PublishAsync(client, ctx, exam.Id);
        blocked.MessagesEnqueued.Should().Be(0);
        (await CountMessagesAsync(ctx.Tenant.Id, NotificationMessageType.MarksReleased)).Should().Be(0);
        (await CountMessagesAsync(ctx.Tenant.Id, NotificationMessageType.ExamScheduled)).Should().Be(0);

        using var onReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/settings/features/ExamNotifications",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain,
            new EduFlow.Api.Endpoints.UpdateFeatureSwitchRequest(true));
        (await client.SendAsync(onReq)).EnsureSuccessStatusCode();
        var released = await PublishAsync(client, ctx, exam.Id);
        released.MessagesEnqueued.Should().Be(2);
        (await CountMessagesAsync(ctx.Tenant.Id, NotificationMessageType.MarksReleased)).Should().Be(2);
    }

    [Fact]
    public async Task Report_cards_are_blocked_when_the_plan_has_no_performance_reports()
    {
        var client = factory.CreateClient();
        var ctx = await SeedAsync(client, "exam-plan", "Starter");
        using var req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/exams/{Guid.NewGuid()}/report-cards",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain);
        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("FEATURE_NOT_IN_PLAN");
    }

    private async Task<int> CountMessagesAsync(Guid tenantId, NotificationMessageType type)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.NotificationOutboxes.IgnoreQueryFilters()
            .CountAsync(m => !m.IsDeleted && m.TenantId == tenantId && m.MessageType == type);
    }

    private static async Task<Guid> DefaultSchemeIdAsync(HttpClient client, ExamSeed ctx)
    {
        var schemes = await SendAsync<List<GradingSchemeDto>>(client, ctx, HttpMethod.Get, "/api/exams/grading-schemes");
        return schemes.Single(s => s.IsDefault).Id;
    }

    private static async Task<ExamDetailDto> CreateExamAsync(
        HttpClient client,
        ExamSeed ctx,
        string name,
        Guid batchId,
        Guid schemeId,
        string series,
        Guid maths,
        Guid science)
    {
        return await SendAsync<ExamDetailDto>(client, ctx, HttpMethod.Post, "/api/exams", new UpsertExamRequest(
            name,
            ExamKind.Term,
            batchId,
            schemeId,
            series,
            true,
            null,
            null,
            [
                new UpsertExamSubjectRequest(null, maths, 100, null, "Hall A", 1),
                new UpsertExamSubjectRequest(null, science, 100, null, "Hall A", 2)
            ]));
    }

    private static async Task ScheduleAsync(HttpClient client, ExamSeed ctx, Guid examId, DateTimeOffset when)
    {
        await SendAsync<ExamDetailDto>(client, ctx, HttpMethod.Post, $"/api/exams/{examId}/schedule", new ScheduleExamRequest(when, "Hall A"));
    }

    private static async Task SaveMarksAsync(
        HttpClient client,
        ExamSeed ctx,
        ExamDetailDto exam,
        Guid studentId,
        decimal maths,
        decimal science,
        string? teacher,
        string? principal)
    {
        var mathsPaper = exam.Subjects.Single(s => s.SubjectId == ctx.Maths).Id;
        var sciencePaper = exam.Subjects.Single(s => s.SubjectId == ctx.Science).Id;
        await SendAsync<MarksGridDto>(client, ctx, HttpMethod.Put, $"/api/exams/{exam.Id}/marks", new SaveMarksRequest(
            [
                new MarkEntryRequest(studentId, mathsPaper, maths, false),
                new MarkEntryRequest(studentId, sciencePaper, science, false)
            ],
            [new StudentRemarkRequest(studentId, teacher, principal)]));
    }

    private static async Task<PublishExamResultDto> PublishAsync(HttpClient client, ExamSeed ctx, Guid examId) =>
        await SendAsync<PublishExamResultDto>(client, ctx, HttpMethod.Post, $"/api/exams/{examId}/publish");

    private static async Task<RankListDto> RanksAsync(HttpClient client, ExamSeed ctx, Guid examId, string scope, Guid? subjectId = null)
    {
        var url = $"/api/exams/{examId}/ranks?scope={scope}";
        if (subjectId is Guid id)
        {
            url += $"&subjectId={id}";
        }

        return await SendAsync<RankListDto>(client, ctx, HttpMethod.Get, url);
    }

    private static async Task<T> SendAsync<T>(HttpClient client, ExamSeed ctx, HttpMethod method, string url, object? body = null)
    {
        using var req = IntegrationTestHelpers.CreateTenantRequest(method, url, ctx.Auth.AccessToken, ctx.Tenant.Subdomain, body);
        var res = await client.SendAsync(req);
        if (res.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Created))
        {
            var text = await res.Content.ReadAsStringAsync();
            res.StatusCode.Should().Be(HttpStatusCode.OK, text);
        }

        if (typeof(T) == typeof(string))
        {
            return (T)(object)(await res.Content.ReadAsStringAsync());
        }

        return (await res.Content.ReadFromJsonAsync<T>(IntegrationTestHelpers.JsonOptions))!;
    }

    private static async Task<ExamSeed> SeedAsync(HttpClient client, string prefix, string plan)
    {
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, prefix, plan);
        var auth = await IntegrationTestHelpers.LoginTenantUserAsync(
            client,
            tenant.Subdomain,
            $"admin@{tenant.Subdomain}.lk",
            "AdminUser@123!");
        var shell = new ExamSeed(auth, tenant, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty);

        using var quietReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/notifications/settings/config",
            auth.AccessToken,
            tenant.Subdomain,
            new UpdateNotificationConfigRequest(
                QuietHoursEnabled: false,
                QuietHoursStartMinutes: 21 * 60,
                QuietHoursEndMinutes: 6 * 60,
                FeeReminderDaysBefore: 3,
                FeeReminderOnDueDate: true,
                FeeReminderDaysAfterOverdue: 3,
                ExamReminderDaysBefore: 1,
                ExamReminderHoursBefore: 0));
        (await client.SendAsync(quietReq)).EnsureSuccessStatusCode();

        var levels = await SendAsync<List<EducationLevelDto>>(client, shell, HttpMethod.Get, "/api/education-levels");
        var levelId = levels.First(l => l.IsEnabled).Id;
        var batchA = await SendAsync<BatchDto>(client, shell, HttpMethod.Post, "/api/batches", new UpsertBatchRequest("Batch A", $"A{Guid.NewGuid().ToString("N")[..4]}", levelId, null, null, "2026"));
        var batchB = await SendAsync<BatchDto>(client, shell, HttpMethod.Post, "/api/batches", new UpsertBatchRequest("Batch B", $"B{Guid.NewGuid().ToString("N")[..4]}", levelId, null, null, "2026"));
        var maths = await SendAsync<SubjectDto>(client, shell, HttpMethod.Post, "/api/subjects", new UpsertSubjectRequest("Combined Maths", $"M{Guid.NewGuid().ToString("N")[..4]}", levelId));
        var science = await SendAsync<SubjectDto>(client, shell, HttpMethod.Post, "/api/subjects", new UpsertSubjectRequest("Science", $"S{Guid.NewGuid().ToString("N")[..4]}", levelId));
        var nimal = await CreateStudentAsync(client, shell, "Nimal Perera", "0771111111", levelId, batchA.Id);
        var amaya = await CreateStudentAsync(client, shell, "Amaya Silva", "0772222222", levelId, batchA.Id);
        var kamal = await CreateStudentAsync(client, shell, "Kamal Fernando", "0773333333", levelId, batchB.Id);
        return shell with
        {
            BatchA = batchA.Id,
            BatchB = batchB.Id,
            Maths = maths.Id,
            Science = science.Id,
            Nimal = nimal,
            Amaya = amaya,
            Kamal = kamal
        };
    }

    private static async Task<Guid> CreateStudentAsync(HttpClient client, ExamSeed ctx, string name, string phone, Guid levelId, Guid batchId)
    {
        var student = await SendAsync<StudentDto>(
            client,
            ctx,
            HttpMethod.Post,
            "/api/students",
            new UpsertStudentRequest(name, "Guardian " + name, phone, "Colombo", levelId, BatchIds: [batchId]));
        return student.Id;
    }

    private sealed record ExamSeed(
        EduFlow.Application.Auth.AuthResponse Auth,
        EduFlow.Application.SuperAdmin.TenantDetailDto Tenant,
        Guid BatchA,
        Guid BatchB,
        Guid Maths,
        Guid Science,
        Guid Nimal,
        Guid Amaya,
        Guid Kamal);
}
