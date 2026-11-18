using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Common.Security;
using EduFlow.Application.Tenancy;
using EduFlow.Domain.Common;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using EduFlow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduFlow.IntegrationTests;

public sealed class TenantIsolationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Reflection_over_all_ITenantEntity_types_enforces_strict_query_and_write_isolation()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);

        var tenantA = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "iso-a", "Pro");
        var tenantB = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "iso-b", "Pro");

        var tenantEntityTypes = typeof(TenantEntity).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ITenantEntity).IsAssignableFrom(t))
            .ToList();

        tenantEntityTypes.Should().Contain(
        [
            typeof(Branch),
            typeof(User),
            typeof(Role),
            typeof(RefreshToken),
            typeof(Subscription),
            typeof(TenantFeatureSetting),
            typeof(TenantFeatureOverride)
        ]);

        using (var seedScope = factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenantContext = seedScope.ServiceProvider.GetRequiredService<ITenantContext>();

            await SeedEveryTenantEntityForTenantAsync(db, tenantContext, tenantA.Id, "a");
            await SeedEveryTenantEntityForTenantAsync(db, tenantContext, tenantB.Id, "b");
        }

        using (var verifyScope = factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenantContext = verifyScope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenantContext.SetTenant(tenantA.Id, tenantA.Subdomain);

            foreach (var entityType in tenantEntityTypes)
            {
                var method = typeof(TenantIsolationTests)
                    .GetMethod(nameof(VerifyEntityIsolationAsync), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(entityType);

                var task = (Task)method.Invoke(null, [db, tenantA.Id, tenantB.Id])!;
                await task;
            }
        }
    }

    [Fact]
    public async Task Tenant_A_can_never_read_or_write_Tenant_B_across_any_API_endpoint()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);

        var tenantA = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "ep-a", "Pro");
        var tenantB = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "ep-b", "Pro");

        var authA = await IntegrationTestHelpers.LoginTenantUserAsync(
            client,
            tenantA.Subdomain,
            $"admin@{tenantA.Subdomain}.lk",
            "AdminUser@123!");

        var authB = await IntegrationTestHelpers.LoginTenantUserAsync(
            client,
            tenantB.Subdomain,
            $"admin@{tenantB.Subdomain}.lk",
            "AdminUser@123!");

        using var createBranchBReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/branches",
            authB.AccessToken,
            tenantB.Subdomain,
            new CreateBranchRequest("Negombo Branch B", "NGB"));
        var createBranchBRes = await client.SendAsync(createBranchBReq);
        createBranchBRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var branchB = (await createBranchBRes.Content.ReadFromJsonAsync<BranchDto>(IntegrationTestHelpers.JsonOptions))!;

        using var createRoleBReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/roles",
            authB.AccessToken,
            tenantB.Subdomain,
            new CreateRoleRequest("Coordinator B", "Role in B", [Permissions.StudentsView]));
        var createRoleBRes = await client.SendAsync(createRoleBReq);
        createRoleBRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var roleB = (await createRoleBRes.Content.ReadFromJsonAsync<RoleDto>(IntegrationTestHelpers.JsonOptions))!;

        using var createUserBReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/users",
            authB.AccessToken,
            tenantB.Subdomain,
            new CreateTenantUserRequest($"staff@{tenantB.Subdomain}.lk", "Staff B", "StaffPass@123!", RoleType.Staff));
        var createUserBRes = await client.SendAsync(createUserBReq);
        createUserBRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var userB = (await createUserBRes.Content.ReadFromJsonAsync<TenantUserDto>(IntegrationTestHelpers.JsonOptions))!;

        using var listBranchesAReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/branches",
            authA.AccessToken,
            tenantA.Subdomain);
        var branchesInA = await (await client.SendAsync(listBranchesAReq))
            .Content.ReadFromJsonAsync<List<BranchDto>>(IntegrationTestHelpers.JsonOptions);
        branchesInA!.Select(b => b.Id).Should().NotContain(branchB.Id);

        using var getBranchBFromAReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/branches/{branchB.Id}",
            authA.AccessToken,
            tenantA.Subdomain);
        (await client.SendAsync(getBranchBFromAReq)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var updateBranchBFromAReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            $"/api/branches/{branchB.Id}",
            authA.AccessToken,
            tenantA.Subdomain,
            new UpdateBranchRequest("Hacked", "HCK"));
        (await client.SendAsync(updateBranchBFromAReq)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var deleteBranchBFromAReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Delete,
            $"/api/branches/{branchB.Id}",
            authA.AccessToken,
            tenantA.Subdomain);
        (await client.SendAsync(deleteBranchBFromAReq)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var getRoleBFromAReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/roles/{roleB.Id}",
            authA.AccessToken,
            tenantA.Subdomain);
        (await client.SendAsync(getRoleBFromAReq)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var deleteUserBFromAReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Delete,
            $"/api/users/{userB.Id}",
            authA.AccessToken,
            tenantA.Subdomain);
        (await client.SendAsync(deleteUserBFromAReq)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var spoofedHeaderReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/branches",
            authA.AccessToken,
            tenantHeader: tenantB.Subdomain);
        var spoofedRes = await client.SendAsync(spoofedHeaderReq);
        spoofedRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var doc = JsonDocument.Parse(await spoofedRes.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("CROSS_TENANT_ACCESS");
    }

    private static async Task SeedEveryTenantEntityForTenantAsync(
        AppDbContext db,
        ITenantContext tenantContext,
        Guid tenantId,
        string suffix)
    {
        using (tenantContext.BeginSystemScope(tenantId))
        {
            var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.TenantId == tenantId);

            db.RefreshTokens.Add(new RefreshToken
            {
                TenantId = tenantId,
                UserId = user.Id,
                TokenHash = PasswordHasher.HashToken($"seed-token-{suffix}-{Guid.NewGuid()}"),
                FamilyId = Guid.NewGuid(),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
            });

            db.TenantFeatureOverrides.Add(new TenantFeatureOverride
            {
                TenantId = tenantId,
                FeatureKey = FeatureKey.WhatsAppMessaging,
                IsEnabled = true,
                Reason = $"Test override {suffix}"
            });

            var level = await db.EducationLevels.IgnoreQueryFilters().OrderBy(item => item.SortOrder).FirstAsync();
            var branch = await db.Branches.IgnoreQueryFilters().FirstAsync(item => !item.IsDeleted && item.TenantId == tenantId);
            db.StaffBranchAssignments.Add(new StaffBranchAssignment
            {
                TenantId = tenantId,
                UserId = user.Id,
                BranchId = branch.Id
            });
            var group = new BatchGroup
            {
                TenantId = tenantId,
                Name = $"Group {suffix}",
                Code = $"G-{suffix}",
                IsActive = true
            };
            var batch = new AcademicBatch
            {
                TenantId = tenantId,
                Name = $"Batch {suffix}",
                Code = $"B-{suffix}",
                EducationLevelId = level.Id,
                BranchId = branch.Id,
                BatchGroupId = group.Id,
                AcademicYear = "2026",
                IsActive = true
            };
            var subject = new Subject
            {
                TenantId = tenantId,
                Name = $"Subject {suffix}",
                Code = $"SUB-{suffix}",
                EducationLevelId = level.Id,
                IsActive = true
            };
            db.AddRange(group, batch, subject);
            await db.SaveChangesAsync();

            var student = new Student
            {
                TenantId = tenantId,
                StudentCode = $"S-{suffix}-0001",
                FullName = $"Student {suffix}",
                GuardianName = $"Guardian {suffix}",
                GuardianPhone = "0771234567",
                Address = "Colombo",
                EducationLevelId = level.Id,
                BranchId = branch.Id,
                Status = StudentStatus.Active,
                Gender = StudentGender.Unspecified,
                AdmissionDate = new DateOnly(2026, 1, 6)
            };
            var schedule = new ClassSchedule
            {
                TenantId = tenantId,
                Name = $"Class {suffix}",
                BatchId = batch.Id,
                SubjectId = subject.Id,
                TeacherUserId = user.Id,
                Weekday = 1,
                StartMinutes = 480,
                EndMinutes = 540,
                Room = "A1",
                IsActive = true
            };
            db.AddRange(student, schedule);
            await db.SaveChangesAsync();

            db.StudentEnrollments.Add(new StudentEnrollment
            {
                TenantId = tenantId,
                StudentId = student.Id,
                BatchId = batch.Id,
                EnrolledOn = student.AdmissionDate,
                IsActive = true
            });
            db.TenantNotificationConfigs.Add(new TenantNotificationConfig
            {
                TenantId = tenantId,
                QuietHoursEnabled = true,
                DefaultLanguage = "en"
            });
            db.TenantNotificationTypeSettings.Add(new TenantNotificationTypeSetting
            {
                TenantId = tenantId,
                MessageType = NotificationMessageType.AttendancePresent,
                IsEnabled = true,
                SmsEnabled = true
            });
            db.NotificationTemplates.Add(new NotificationTemplate
            {
                TenantId = tenantId,
                MessageType = NotificationMessageType.AttendancePresent,
                Language = "en",
                Subject = $"Subject {suffix}",
                Body = $"Body {suffix}",
                IsCustom = true
            });
            db.AttendancePolicies.Add(new AttendancePolicy
            {
                TenantId = tenantId,
                LateAfterMinutes = 10,
                MarkAbsentOnStop = true
            });
            var classSession = new ClassSession
            {
                TenantId = tenantId,
                ClassScheduleId = schedule.Id,
                SessionDate = new DateOnly(2026, 11, 17),
                Status = ClassSessionStatus.Scheduled
            };
            db.ClassSessions.Add(classSession);
            await db.SaveChangesAsync();
            db.AttendanceMarks.Add(new AttendanceMark
            {
                TenantId = tenantId,
                ClassSessionId = classSession.Id,
                StudentId = student.Id,
                Status = AttendanceMarkStatus.Present,
                Source = AttendanceMarkSource.Manual,
                MarkedAt = DateTimeOffset.UtcNow
            });
            db.NotificationOutboxes.Add(new NotificationOutbox
            {
                TenantId = tenantId,
                MessageType = NotificationMessageType.AttendancePresent,
                Channel = NotificationChannel.Sms,
                Status = NotificationStatus.Queued,
                Language = "en",
                Recipient = "0771234567",
                Body = $"Message {suffix}",
                IdempotencyKey = $"idem-{suffix}-{Guid.NewGuid():N}",
                SmsSegments = 1
            });

            var feeStructure = new FeeStructure
            {
                TenantId = tenantId,
                Name = $"Tuition Fee {suffix}",
                Frequency = FeeFrequency.Monthly,
                Amount = 3500m,
                Currency = "LKR",
                EducationLevelId = level.Id,
                BatchId = batch.Id,
                DueDayOfMonth = 10,
                IsActive = true
            };
            db.FeeStructures.Add(feeStructure);
            await db.SaveChangesAsync();

            var concession = new StudentConcession
            {
                TenantId = tenantId,
                StudentId = student.Id,
                FeeStructureId = feeStructure.Id,
                Name = $"Scholarship {suffix}",
                DiscountType = DiscountType.Percentage,
                Value = 10m,
                IsActive = true
            };
            var feeInvoice = new FeeInvoice
            {
                TenantId = tenantId,
                InvoiceNumber = $"INV-{suffix}-0001",
                StudentId = student.Id,
                FeeStructureId = feeStructure.Id,
                BatchId = batch.Id,
                EducationLevelId = level.Id,
                BillingPeriod = "2026-11",
                Title = $"Tuition Fee {suffix} (2026-11)",
                GrossAmount = 3500m,
                DiscountAmount = 350m,
                NetAmount = 3150m,
                PaidAmount = 1000m,
                BalanceAmount = 2150m,
                Currency = "LKR",
                IssueDate = new DateOnly(2026, 11, 1),
                DueDate = new DateOnly(2026, 11, 10),
                Status = FeeInvoiceStatus.PartiallyPaid
            };
            db.AddRange(concession, feeInvoice);
            await db.SaveChangesAsync();

            var feePayment = new FeePayment
            {
                TenantId = tenantId,
                InvoiceId = feeInvoice.Id,
                StudentId = student.Id,
                ReceiptSequence = 1,
                ReceiptNumber = $"RCP-{suffix}-0001",
                Amount = 1000m,
                RefundedAmount = 0m,
                Currency = "LKR",
                Method = PaymentMethod.Cash,
                Status = PaymentRecordStatus.Completed,
                PaidAt = DateTimeOffset.UtcNow
            };
            var gatewayConfig = new TenantPaymentGatewayConfig
            {
                TenantId = tenantId,
                MerchantId = $"M-{suffix}",
                EncryptedMerchantSecret = $"ENC-{suffix}",
                IsSandbox = true,
                IsEnabled = true
            };
            var checkoutSession = new PayHereCheckoutSession
            {
                TenantId = tenantId,
                OrderId = $"PH-{suffix}-{Guid.NewGuid():N}",
                InvoiceId = feeInvoice.Id,
                StudentId = student.Id,
                Amount = 2150m,
                Currency = "LKR",
                IsSandbox = true,
                Status = PayHereCheckoutStatus.Initiated
            };
            var sub = await db.Subscriptions.IgnoreQueryFilters().FirstAsync(s => s.TenantId == tenantId);
            var platformInvoice = new PlatformSubscriptionInvoice
            {
                TenantId = tenantId,
                InvoiceNumber = $"PLAT-{suffix}-{Guid.NewGuid().ToString("N")[..6]}",
                SubscriptionId = sub.Id,
                PlanId = sub.PlanId,
                PlanName = "Pro",
                Amount = 15000m,
                Currency = "LKR",
                PeriodMonths = 1,
                DaysToExtend = 30,
                IssueDate = new DateOnly(2026, 11, 1),
                DueDate = new DateOnly(2026, 11, 15),
                Status = PlatformInvoiceStatus.Pending
            };
            db.AddRange(feePayment, gatewayConfig, checkoutSession, platformInvoice);
            await db.SaveChangesAsync();

            var gradingScheme = new GradingScheme
            {
                TenantId = tenantId,
                Name = $"Scheme {suffix}",
                IsDefault = true
            };
            db.GradingSchemes.Add(gradingScheme);
            await db.SaveChangesAsync();
            var exam = new Exam
            {
                TenantId = tenantId,
                Name = $"Exam {suffix}",
                Kind = ExamKind.Term,
                BatchId = batch.Id,
                EducationLevelId = level.Id,
                GradingSchemeId = gradingScheme.Id,
                SeriesKey = $"series-{suffix}",
                Status = ExamStatus.Draft
            };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();
            var examSubject = new ExamSubject
            {
                TenantId = tenantId,
                ExamId = exam.Id,
                SubjectId = subject.Id,
                MaxMarks = 100m,
                SortOrder = 1
            };
            db.ExamSubjects.Add(examSubject);
            db.GradeBoundaries.Add(new GradeBoundary
            {
                TenantId = tenantId,
                GradingSchemeId = gradingScheme.Id,
                Grade = "A",
                MinPercent = 0m,
                MaxPercent = 100m,
                SortOrder = 1
            });
            await db.SaveChangesAsync();
            db.ExamMarks.Add(new ExamMark
            {
                TenantId = tenantId,
                ExamId = exam.Id,
                ExamSubjectId = examSubject.Id,
                StudentId = student.Id,
                Marks = 80m
            });
            db.ExamResults.Add(new ExamResult
            {
                TenantId = tenantId,
                ExamId = exam.Id,
                StudentId = student.Id,
                TotalMarks = 80m,
                MaxMarks = 100m,
                Percentage = 80m,
                Grade = "A"
            });
            db.ParentStudentLinks.Add(new ParentStudentLink
            {
                TenantId = tenantId,
                ParentUserId = user.Id,
                StudentId = student.Id
            });
            db.TenantBackupPolicies.Add(new TenantBackupPolicy
            {
                TenantId = tenantId,
                IsScheduledEnabled = true,
                RetentionDays = 30,
                MaxBackupsToKeep = 10
            });
            db.TenantBackupRecords.Add(new TenantBackupRecord
            {
                TenantId = tenantId,
                Trigger = BackupTrigger.Manual,
                Status = BackupStatus.Completed,
                FileName = $"backup-{suffix}.zip",
                StorageUrl = $"/files/backups/{tenantId:N}/backup-{suffix}.zip",
                SizeBytes = 1024,
                Sha256 = "abc123",
                TotalRecords = 5,
                SummaryJson = "{}"
            });
            db.TenantCleanupPolicies.Add(new TenantCleanupPolicy
            {
                TenantId = tenantId,
                AuditLogRetentionDays = 90,
                NotificationRetentionDays = 90,
                SoftDeletedRetentionDays = 30
            });
            db.StaffAttendanceRecords.Add(new StaffAttendanceRecord
            {
                TenantId = tenantId,
                StaffUserId = user.Id,
                Date = new DateOnly(2026, 11, 17),
                Status = StaffAttendanceStatus.Present
            });
            db.StaffLeaveRequests.Add(new StaffLeaveRequest
            {
                TenantId = tenantId,
                StaffUserId = user.Id,
                LeaveType = StaffLeaveType.Casual,
                StartDate = new DateOnly(2026, 11, 20),
                EndDate = new DateOnly(2026, 11, 21),
                Reason = $"Leave {suffix}",
                Status = StaffLeaveStatus.Pending
            });
            db.TenantOnboardings.Add(new TenantOnboarding
            {
                TenantId = tenantId,
                CardsPrintedAt = DateTimeOffset.UtcNow
            });
            db.TenantAddOns.Add(new TenantAddOn
            {
                TenantId = tenantId,
                Type = TenantAddOnType.ExtraSmsSegments,
                Quantity = 10,
                ValidFrom = DateTimeOffset.UtcNow.AddDays(-1),
                Note = $"Add-on {suffix}"
            });
            db.StoredFileRecords.Add(new StoredFileRecord
            {
                TenantId = tenantId,
                Category = StoredFileCategory.StudentPhoto,
                OwnerId = student.Id,
                Slot = "photo",
                StorageKey = $"legacy/students/{suffix}",
                FileName = "photo.jpg",
                ContentType = "image/jpeg",
                SizeBytes = 128
            });
            var campaign = new BroadcastCampaign
            {
                TenantId = tenantId,
                Title = $"Notice {suffix}",
                AudienceKind = BroadcastAudienceKind.All,
                Channel = NotificationChannel.Sms,
                Language = "en",
                Subject = "Announcement",
                BodyTemplate = "{message}",
                Message = $"Hello {suffix}",
                ScheduledAt = DateTimeOffset.UtcNow,
                Status = BroadcastCampaignStatus.Queued,
                RecipientCount = 1
            };
            db.BroadcastCampaigns.Add(campaign);
            db.BroadcastRecipients.Add(new BroadcastRecipient
            {
                TenantId = tenantId,
                CampaignId = campaign.Id,
                StudentId = student.Id,
                StudentName = student.FullName,
                Recipient = student.GuardianPhone,
                Channel = NotificationChannel.Sms,
                Status = NotificationStatus.Queued,
                SmsSegments = 1
            });
            await db.SaveChangesAsync();
        }
    }

    private static async Task VerifyEntityIsolationAsync<TEntity>(
        AppDbContext db,
        Guid tenantAId,
        Guid tenantBId)
        where TEntity : BaseEntity, ITenantEntity
    {
        var visibleRows = await db.Set<TEntity>().AsNoTracking().ToListAsync();
        visibleRows.Should().NotBeEmpty($"Tenant A should have seeded rows for {typeof(TEntity).Name}");
        visibleRows.Should().OnlyContain(
            e => e.TenantId == tenantAId,
            $"Querying {typeof(TEntity).Name} in Tenant A must never return rows from Tenant B");

        var tenantBRow = await db.Set<TEntity>()
            .IgnoreQueryFilters()
            .FirstAsync(e => !e.IsDeleted && e.TenantId == tenantBId);

        db.Set<TEntity>().Remove(tenantBRow);
        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<CrossTenantOperationException>(
            $"Deleting Tenant B's {typeof(TEntity).Name} while scoped to Tenant A must be blocked");

        db.Entry(tenantBRow).State = EntityState.Detached;
    }
}
