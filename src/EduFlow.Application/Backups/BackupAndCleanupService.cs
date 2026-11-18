using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EduFlow.Application.Academics;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Notifications;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Backups;

public interface IBackupAndCleanupService
{
    Task<TenantBackupWorkspaceDto> GetBackupWorkspaceAsync(CancellationToken cancellationToken = default);
    Task<TenantBackupPolicyDto> UpdateBackupPolicyAsync(UpdateTenantBackupPolicyRequest request, CancellationToken cancellationToken = default);
    Task<TenantBackupRecordDto?> RunTenantBackupAsync(Guid? tenantIdOverride = null, BackupTrigger trigger = BackupTrigger.Manual, CancellationToken cancellationToken = default);
    Task<ScheduledBackupBatchResultDto> RunScheduledTenantBackupsAsync(DateTimeOffset? nowOverride = null, CancellationToken cancellationToken = default);
    Task<StoredDownload> DownloadTenantBackupAsync(Guid backupId, Guid? tenantIdOverride = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantBackupRecordDto>> GetTenantBackupsForSupportAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<BackupRestoreResultDto> RestoreTenantBackupAsync(Guid tenantId, Guid backupId, RestoreTenantBackupRequest request, CancellationToken cancellationToken = default);

    Task<PlatformBackupWorkspaceDto> GetPlatformBackupsAsync(CancellationToken cancellationToken = default);
    Task<PlatformBackupWorkspaceDto> UpdatePlatformBackupRetentionAsync(int retentionDays, CancellationToken cancellationToken = default);
    Task<PlatformBackupRecordDto> RunPlatformBackupAsync(BackupTrigger trigger = BackupTrigger.Manual, int? retentionDaysOverride = null, CancellationToken cancellationToken = default);
    Task<StoredDownload> DownloadPlatformBackupAsync(Guid backupId, CancellationToken cancellationToken = default);

    Task<ReportCleanupWorkspaceDto> GetCleanupWorkspaceAsync(CancellationToken cancellationToken = default);
    Task<TenantCleanupPolicyDto> UpdateCleanupPolicyAsync(UpdateTenantCleanupPolicyRequest request, CancellationToken cancellationToken = default);
    Task<ReportCleanupResultDto> RunReportCleanupAsync(RunReportCleanupRequest request, CancellationToken cancellationToken = default);
}

public sealed record TenantBackupSnapshot(
    Guid TenantId,
    string Subdomain,
    string Name,
    DateTimeOffset ExportedAtUtc,
    IReadOnlyList<Branch> Branches,
    IReadOnlyList<AcademicBatch> Batches,
    IReadOnlyList<Subject> Subjects,
    IReadOnlyList<ClassSchedule> ClassSchedules,
    IReadOnlyList<Student> Students,
    IReadOnlyList<StudentEnrollment> StudentEnrollments,
    IReadOnlyList<ClassSession> ClassSessions,
    IReadOnlyList<AttendanceMark> AttendanceMarks,
    IReadOnlyList<FeeStructure> FeeStructures,
    IReadOnlyList<FeeInvoice> FeeInvoices,
    IReadOnlyList<FeePayment> FeePayments,
    IReadOnlyList<Exam> Exams,
    IReadOnlyList<ExamSubject> ExamSubjects,
    IReadOnlyList<ExamMark> ExamMarks,
    IReadOnlyList<ExamResult> ExamResults,
    IReadOnlyList<StaffAttendanceRecord> StaffAttendances,
    IReadOnlyList<StaffLeaveRequest> StaffLeaves);

public sealed class BackupAndCleanupService(
    IAppDbContext db,
    ITenantContext tenantContext,
    IFeatureService featureService,
    IFileStorage fileStorage,
    IDatabaseBackupExecutor dbBackupExecutor,
    INotificationService notificationService,
    INotificationDispatcher notificationDispatcher,
    IAuditService auditService) : IBackupAndCleanupService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<TenantBackupWorkspaceDto> GetBackupWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = await EnsureAutoBackupEnabledAsync(cancellationToken);
        var policy = await GetOrCreateBackupPolicyAsync(tenantId, cancellationToken);
        var history = await db.TenantBackupRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => !r.IsDeleted && r.TenantId == tenantId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        return new TenantBackupWorkspaceDto(
            MapBackupPolicy(policy),
            history.Select(MapTenantBackupRecord).ToList());
    }

    public async Task<TenantBackupPolicyDto> UpdateBackupPolicyAsync(
        UpdateTenantBackupPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = await EnsureAutoBackupEnabledAsync(cancellationToken);

        if (request.RetentionDays is < 1 or > 365)
        {
            throw new DomainValidationException("INVALID_RETENTION", "Backup retention days must be between 1 and 365.");
        }

        if (request.MaxBackupsToKeep is < 1 or > 100)
        {
            throw new DomainValidationException("INVALID_MAX_BACKUPS", "Max backups to keep must be between 1 and 100.");
        }

        if (request.PreferredHourUtc is < 0 or > 23)
        {
            throw new DomainValidationException("INVALID_HOUR", "Preferred backup hour (UTC) must be between 0 and 23.");
        }

        using (tenantContext.BeginSystemScope(tenantId))
        {
            var policy = await GetOrCreateBackupPolicyAsync(tenantId, cancellationToken);
            policy.IsScheduledEnabled = request.IsScheduledEnabled;
            policy.RetentionDays = request.RetentionDays;
            policy.MaxBackupsToKeep = request.MaxBackupsToKeep;
            policy.PreferredHourUtc = request.PreferredHourUtc;

            await ApplyTenantRetentionAsync(tenantId, policy, DateTimeOffset.UtcNow, null, cancellationToken);

            await auditService.LogAsync(
                "BackupPolicy.Updated",
                nameof(TenantBackupPolicy),
                policy.Id.ToString(),
                new { policy.IsScheduledEnabled, policy.RetentionDays, policy.MaxBackupsToKeep, policy.PreferredHourUtc },
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            return MapBackupPolicy(policy);
        }
    }

    public async Task<TenantBackupRecordDto?> RunTenantBackupAsync(
        Guid? tenantIdOverride = null,
        BackupTrigger trigger = BackupTrigger.Manual,
        CancellationToken cancellationToken = default)
    {
        var tenantId = tenantIdOverride ?? TenantGuard.Require(tenantContext);

        if (trigger == BackupTrigger.Manual)
        {
            var eval = await featureService.EvaluateAsync(tenantId, FeatureKey.AutoBackup, cancellationToken);
            if (!eval.IsInPlan)
            {
                throw new FeatureNotInPlanException(FeatureKey.AutoBackup);
            }

            if (!eval.IsEnabled)
            {
                throw new FeatureDisabledException(FeatureKey.AutoBackup);
            }
        }
        else
        {
            var enabled = await featureService.IsEnabledAsync(tenantId, FeatureKey.AutoBackup, cancellationToken);
            if (!enabled)
            {
                return null;
            }
        }

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => !t.IsDeleted && t.Id == tenantId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Tenant), tenantId.ToString());

        var now = DateTimeOffset.UtcNow;

        using (tenantContext.BeginSystemScope(tenantId))
        {
            var policy = await GetOrCreateBackupPolicyAsync(tenantId, cancellationToken);
            if (trigger == BackupTrigger.Scheduled && !policy.IsScheduledEnabled)
            {
                return null;
            }

            var branches = await db.Branches.IgnoreQueryFilters().AsNoTracking().Where(x => !x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken);
            var batches = await db.AcademicBatches.IgnoreQueryFilters().AsNoTracking().Where(x => !x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken);
            var subjects = await db.Subjects.IgnoreQueryFilters().AsNoTracking().Where(x => !x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken);
            var schedules = await db.ClassSchedules.IgnoreQueryFilters().AsNoTracking().Where(x => !x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken);
            var students = await db.Students.IgnoreQueryFilters().AsNoTracking().Where(x => !x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken);
            var enrollments = await db.StudentEnrollments.IgnoreQueryFilters().AsNoTracking().Where(x => !x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken);
            var sessions = await db.ClassSessions.IgnoreQueryFilters().AsNoTracking().Where(x => !x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken);
            var attendanceMarks = await db.AttendanceMarks.IgnoreQueryFilters().AsNoTracking().Where(x => !x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken);
            var feeStructures = await db.FeeStructures.IgnoreQueryFilters().AsNoTracking().Where(x => !x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken);
            var feeInvoices = await db.FeeInvoices.IgnoreQueryFilters().AsNoTracking().Where(x => !x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken);
            var feePayments = await db.FeePayments.IgnoreQueryFilters().AsNoTracking().Where(x => !x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken);
            var exams = await db.Exams.IgnoreQueryFilters().AsNoTracking().Where(x => !x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken);
            var examSubjects = await db.ExamSubjects.IgnoreQueryFilters().AsNoTracking().Where(x => !x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken);
            var examMarks = await db.ExamMarks.IgnoreQueryFilters().AsNoTracking().Where(x => !x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken);
            var examResults = await db.ExamResults.IgnoreQueryFilters().AsNoTracking().Where(x => !x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken);
            var staffAttendances = await db.StaffAttendanceRecords.IgnoreQueryFilters().AsNoTracking().Where(x => !x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken);
            var staffLeaves = await db.StaffLeaveRequests.IgnoreQueryFilters().AsNoTracking().Where(x => !x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken);
            var branding = await db.BrandingProfiles.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => !x.IsDeleted && x.TenantId == tenantId, cancellationToken);

            var uploadedFiles = new List<UploadedFileManifestItem>();
            foreach (var s in students.Where(s => !string.IsNullOrWhiteSpace(s.PhotoUrl)))
            {
                uploadedFiles.Add(new UploadedFileManifestItem("StudentPhoto", s.Id, s.StudentCode, s.PhotoUrl!, s.PhotoBytes));
            }

            foreach (var p in feePayments.Where(p => !string.IsNullOrWhiteSpace(p.AttachmentUrl)))
            {
                uploadedFiles.Add(new UploadedFileManifestItem("PaymentAttachment", p.Id, p.ReceiptNumber, p.AttachmentUrl!, p.AttachmentBytes));
            }

            if (branding is not null)
            {
                if (!string.IsNullOrWhiteSpace(branding.LogoUrl))
                {
                    uploadedFiles.Add(new UploadedFileManifestItem("BrandingLogo", branding.Id, "logo", branding.LogoUrl!, 0));
                }

                if (!string.IsNullOrWhiteSpace(branding.FaviconUrl))
                {
                    uploadedFiles.Add(new UploadedFileManifestItem("BrandingFavicon", branding.Id, "favicon", branding.FaviconUrl!, 0));
                }
            }

            var counts = new Dictionary<string, int>
            {
                ["branches"] = branches.Count,
                ["batches"] = batches.Count,
                ["subjects"] = subjects.Count,
                ["classSchedules"] = schedules.Count,
                ["students"] = students.Count,
                ["enrollments"] = enrollments.Count,
                ["classSessions"] = sessions.Count,
                ["attendanceMarks"] = attendanceMarks.Count,
                ["feeStructures"] = feeStructures.Count,
                ["feeInvoices"] = feeInvoices.Count,
                ["feePayments"] = feePayments.Count,
                ["exams"] = exams.Count,
                ["examMarks"] = examMarks.Count,
                ["examResults"] = examResults.Count,
                ["staffAttendances"] = staffAttendances.Count,
                ["staffLeaves"] = staffLeaves.Count
            };

            var totalRecords = counts.Values.Sum();
            var manifest = new TenantBackupManifest(
                FormatVersion: 1,
                TenantId: tenantId,
                TenantSubdomain: tenant.Subdomain,
                TenantName: tenant.Name,
                CreatedAtUtc: now,
                Trigger: trigger.ToString(),
                TotalRecords: totalRecords,
                UploadedFilesCount: uploadedFiles.Count,
                Counts: counts);

            var snapshot = new TenantBackupSnapshot(
                tenantId,
                tenant.Subdomain,
                tenant.Name,
                now,
                branches,
                batches,
                subjects,
                schedules,
                students,
                enrollments,
                sessions,
                attendanceMarks,
                feeStructures,
                feeInvoices,
                feePayments,
                exams,
                examSubjects,
                examMarks,
                examResults,
                staffAttendances,
                staffLeaves);

            byte[] zipBytes;
            await using (var memory = new MemoryStream())
            {
                using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
                {
                    await WriteZipTextEntryAsync(archive, "manifest.json", JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
                    await WriteZipTextEntryAsync(archive, "data/snapshot.json", JsonSerializer.Serialize(snapshot, JsonOptions), cancellationToken);
                    await WriteZipTextEntryAsync(archive, "files_manifest.json", JsonSerializer.Serialize(uploadedFiles, JsonOptions), cancellationToken);

                    await WriteZipTextEntryAsync(
                        archive,
                        "csv/students.csv",
                        BuildCsv(
                            ["StudentCode", "FullName", "GuardianName", "GuardianPhone", "City", "Status", "AdmissionDate"],
                            students.Select(s => (IReadOnlyList<string>)[s.StudentCode, s.FullName, s.GuardianName, s.GuardianPhone, s.City ?? "", s.Status.ToString(), s.AdmissionDate.ToString("yyyy-MM-dd")])),
                        cancellationToken);

                    await WriteZipTextEntryAsync(
                        archive,
                        "csv/batches.csv",
                        BuildCsv(
                            ["Code", "Name", "AcademicYear", "IsActive"],
                            batches.Select(b => (IReadOnlyList<string>)[b.Code, b.Name, b.AcademicYear ?? "", b.IsActive ? "true" : "false"])),
                        cancellationToken);

                    await WriteZipTextEntryAsync(
                        archive,
                        "csv/attendance.csv",
                        BuildCsv(
                            ["SessionId", "StudentId", "Status", "Source", "MarkedAt"],
                            attendanceMarks.Select(a => (IReadOnlyList<string>)[a.ClassSessionId.ToString(), a.StudentId.ToString(), a.Status.ToString(), a.Source.ToString(), a.MarkedAt.ToString("O")])),
                        cancellationToken);

                    await WriteZipTextEntryAsync(
                        archive,
                        "csv/fee_invoices.csv",
                        BuildCsv(
                            ["InvoiceNumber", "StudentId", "BillingPeriod", "NetAmount", "PaidAmount", "BalanceAmount", "DueDate", "Status"],
                            feeInvoices.Select(i => (IReadOnlyList<string>)[i.InvoiceNumber, i.StudentId.ToString(), i.BillingPeriod, i.NetAmount.ToString("0.00"), i.PaidAmount.ToString("0.00"), i.BalanceAmount.ToString("0.00"), i.DueDate.ToString("yyyy-MM-dd"), i.Status.ToString()])),
                        cancellationToken);

                    await WriteZipTextEntryAsync(
                        archive,
                        "csv/fee_payments.csv",
                        BuildCsv(
                            ["ReceiptNumber", "InvoiceId", "StudentId", "Amount", "Method", "Status", "PaidAt"],
                            feePayments.Select(p => (IReadOnlyList<string>)[p.ReceiptNumber, p.InvoiceId.ToString(), p.StudentId.ToString(), p.Amount.ToString("0.00"), p.Method.ToString(), p.Status.ToString(), p.PaidAt.ToString("O")])),
                        cancellationToken);

                    await WriteZipTextEntryAsync(
                        archive,
                        "csv/exams.csv",
                        BuildCsv(
                            ["Name", "Kind", "SeriesKey", "Status"],
                            exams.Select(e => (IReadOnlyList<string>)[e.Name, e.Kind.ToString(), e.SeriesKey, e.Status.ToString()])),
                        cancellationToken);
                }

                zipBytes = memory.ToArray();
            }

            var sha256 = Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();
            var fileName = $"backup-{tenant.Subdomain}-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}.zip";
            await using var contentStream = new MemoryStream(zipBytes);
            var stored = await fileStorage.SaveAsync($"backups/{tenantId:N}", fileName, contentStream, cancellationToken);

            var record = new TenantBackupRecord
            {
                TenantId = tenantId,
                Trigger = trigger,
                Status = BackupStatus.Completed,
                FileName = fileName,
                StorageUrl = stored.Url,
                SizeBytes = stored.SizeBytes,
                Sha256 = sha256,
                TotalRecords = totalRecords,
                UploadedFilesCount = uploadedFiles.Count,
                SummaryJson = JsonSerializer.Serialize(counts),
                CompletedAt = now
            };

            db.TenantBackupRecords.Add(record);
            await db.SaveChangesAsync(cancellationToken);

            await ApplyTenantRetentionAsync(tenantId, policy, now, record.Id, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            await notificationService.EnqueueAsync(
                new EnqueueNotificationRequest(
                    NotificationMessageType.BackupResultAdmin,
                    RecipientEmail: tenant.ContactEmail ?? $"admin@{tenant.Subdomain}.admino.lk",
                    RecipientName: tenant.Name,
                    Variables: new Dictionary<string, string>
                    {
                        ["instituteName"] = tenant.Name,
                        ["date"] = now.ToString("yyyy-MM-dd"),
                        ["backupStatus"] = $"Completed ({totalRecords} records)"
                    },
                    IdempotencyKey: $"backup-result-{record.Id:N}"),
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);

            await notificationDispatcher.DispatchPendingAsync(now, tenantId, cancellationToken);

            await auditService.LogAsync(
                "TenantBackup.Completed",
                nameof(TenantBackupRecord),
                record.Id.ToString(),
                new { record.FileName, record.SizeBytes, record.TotalRecords, Trigger = trigger.ToString() },
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            return MapTenantBackupRecord(record);
        }
    }

    public async Task<ScheduledBackupBatchResultDto> RunScheduledTenantBackupsAsync(
        DateTimeOffset? nowOverride = null,
        CancellationToken cancellationToken = default)
    {
        var tenants = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => !t.IsDeleted && t.IsActive)
            .ToListAsync(cancellationToken);

        var created = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var tenant in tenants)
        {
            var isFeatureOn = await featureService.IsEnabledAsync(tenant.Id, FeatureKey.AutoBackup, cancellationToken);
            if (!isFeatureOn)
            {
                skipped++;
                continue;
            }

            try
            {
                var result = await RunTenantBackupAsync(tenant.Id, BackupTrigger.Scheduled, cancellationToken);
                if (result is null)
                {
                    skipped++;
                }
                else
                {
                    created++;
                }
            }
            catch
            {
                failed++;
            }
        }

        return new ScheduledBackupBatchResultDto(tenants.Count, created, skipped, failed);
    }

    public async Task<StoredDownload> DownloadTenantBackupAsync(
        Guid backupId,
        Guid? tenantIdOverride = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = tenantIdOverride ?? await EnsureAutoBackupEnabledAsync(cancellationToken);
        var record = await db.TenantBackupRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => !r.IsDeleted && r.TenantId == tenantId && r.Id == backupId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(TenantBackupRecord), backupId.ToString());

        await using var stream = fileStorage.OpenRead(record.StorageUrl)
            ?? throw new EntityNotFoundException("BackupFile", record.FileName);
        await using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);

        return new StoredDownload(memory.ToArray(), "application/zip", record.FileName);
    }

    public async Task<IReadOnlyList<TenantBackupRecordDto>> GetTenantBackupsForSupportAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var exists = await db.Tenants
            .IgnoreQueryFilters()
            .AnyAsync(t => !t.IsDeleted && t.Id == tenantId, cancellationToken);
        if (!exists)
        {
            throw new EntityNotFoundException(nameof(Tenant), tenantId.ToString());
        }

        var records = await db.TenantBackupRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => !r.IsDeleted && r.TenantId == tenantId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        return records.Select(MapTenantBackupRecord).ToList();
    }

    public async Task<BackupRestoreResultDto> RestoreTenantBackupAsync(
        Guid tenantId,
        Guid backupId,
        RestoreTenantBackupRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => !t.IsDeleted && t.Id == tenantId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Tenant), tenantId.ToString());

        var record = await db.TenantBackupRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => !r.IsDeleted && r.Id == backupId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(TenantBackupRecord), backupId.ToString());

        if (record.TenantId != tenantId)
        {
            throw new CrossTenantOperationException("Cannot restore a backup archive belonging to another tenant.");
        }

        await using var stream = fileStorage.OpenRead(record.StorageUrl)
            ?? throw new EntityNotFoundException("BackupFile", record.FileName);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new DomainValidationException("INVALID_BACKUP_ARCHIVE", "Backup archive is missing manifest.json.");
        var snapshotEntry = archive.GetEntry("data/snapshot.json")
            ?? throw new DomainValidationException("INVALID_BACKUP_ARCHIVE", "Backup archive is missing data/snapshot.json.");

        TenantBackupManifest manifest;
        await using (var manifestStream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<TenantBackupManifest>(manifestStream, JsonOptions, cancellationToken)
                ?? throw new DomainValidationException("INVALID_BACKUP_ARCHIVE", "Could not parse manifest.json.");
        }

        TenantBackupSnapshot snapshot;
        await using (var snapshotStream = snapshotEntry.Open())
        {
            snapshot = await JsonSerializer.DeserializeAsync<TenantBackupSnapshot>(snapshotStream, JsonOptions, cancellationToken)
                ?? throw new DomainValidationException("INVALID_BACKUP_ARCHIVE", "Could not parse data/snapshot.json.");
        }

        if (manifest.TenantId != tenantId || snapshot.TenantId != tenantId)
        {
            throw new CrossTenantOperationException("Archive tenant identifier does not match target tenant.");
        }

        if (snapshot.Students.Any(x => x.TenantId != tenantId)
            || snapshot.Batches.Any(x => x.TenantId != tenantId)
            || snapshot.Subjects.Any(x => x.TenantId != tenantId)
            || snapshot.FeeInvoices.Any(x => x.TenantId != tenantId)
            || snapshot.Exams.Any(x => x.TenantId != tenantId))
        {
            throw new CrossTenantOperationException("Archive contains entities belonging to another tenant.");
        }

        using (tenantContext.BeginSystemScope(tenantId))
        {
            var existingBranches = await db.Branches.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.Id, cancellationToken);
            var existingBatches = await db.AcademicBatches.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.Id, cancellationToken);
            var existingSubjects = await db.Subjects.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.Id, cancellationToken);
            var existingStudents = await db.Students.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.Id, cancellationToken);
            var existingEnrollments = await db.StudentEnrollments.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.Id, cancellationToken);
            var existingSchedules = await db.ClassSchedules.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.Id, cancellationToken);

            var restorableBranches = snapshot.Branches.Where(b => !existingBranches.TryGetValue(b.Id, out var ex) || ex.IsDeleted).ToList();
            var restorableBatches = snapshot.Batches.Where(b => !existingBatches.TryGetValue(b.Id, out var ex) || ex.IsDeleted).ToList();
            var restorableSubjects = snapshot.Subjects.Where(s => !existingSubjects.TryGetValue(s.Id, out var ex) || ex.IsDeleted).ToList();
            var restorableStudents = snapshot.Students.Where(s => !existingStudents.TryGetValue(s.Id, out var ex) || ex.IsDeleted).ToList();
            var restorableEnrollments = snapshot.StudentEnrollments.Where(e => !existingEnrollments.TryGetValue(e.Id, out var ex) || ex.IsDeleted).ToList();
            var restorableSchedules = snapshot.ClassSchedules.Where(c => !existingSchedules.TryGetValue(c.Id, out var ex) || ex.IsDeleted).ToList();

            var restorableCounts = new Dictionary<string, int>
            {
                ["branches"] = restorableBranches.Count,
                ["batches"] = restorableBatches.Count,
                ["subjects"] = restorableSubjects.Count,
                ["students"] = restorableStudents.Count,
                ["enrollments"] = restorableEnrollments.Count,
                ["classSchedules"] = restorableSchedules.Count
            };

            var warnings = new List<string>();
            if (manifest.UploadedFilesCount > 0)
            {
                warnings.Add($"Archive references {manifest.UploadedFilesCount} uploaded file(s) in files_manifest.json.");
            }

            var restoredTotal = 0;

            if (!request.DryRun)
            {
                foreach (var item in restorableBranches)
                {
                    if (existingBranches.TryGetValue(item.Id, out var tracked))
                    {
                        tracked.IsDeleted = false;
                        tracked.Name = item.Name;
                    }
                    else
                    {
                        item.IsDeleted = false;
                        db.Branches.Add(item);
                    }
                    restoredTotal++;
                }

                foreach (var item in restorableBatches)
                {
                    if (existingBatches.TryGetValue(item.Id, out var tracked))
                    {
                        tracked.IsDeleted = false;
                        tracked.Name = item.Name;
                    }
                    else
                    {
                        item.IsDeleted = false;
                        db.AcademicBatches.Add(item);
                    }
                    restoredTotal++;
                }

                foreach (var item in restorableSubjects)
                {
                    if (existingSubjects.TryGetValue(item.Id, out var tracked))
                    {
                        tracked.IsDeleted = false;
                        tracked.Name = item.Name;
                    }
                    else
                    {
                        item.IsDeleted = false;
                        db.Subjects.Add(item);
                    }
                    restoredTotal++;
                }

                foreach (var item in restorableStudents)
                {
                    if (existingStudents.TryGetValue(item.Id, out var tracked))
                    {
                        tracked.IsDeleted = false;
                        tracked.FullName = item.FullName;
                        tracked.GuardianName = item.GuardianName;
                        tracked.GuardianPhone = item.GuardianPhone;
                    }
                    else
                    {
                        item.IsDeleted = false;
                        db.Students.Add(item);
                    }
                    restoredTotal++;
                }

                foreach (var item in restorableEnrollments)
                {
                    if (existingEnrollments.TryGetValue(item.Id, out var tracked))
                    {
                        tracked.IsDeleted = false;
                        tracked.IsActive = item.IsActive;
                    }
                    else
                    {
                        item.IsDeleted = false;
                        db.StudentEnrollments.Add(item);
                    }
                    restoredTotal++;
                }

                foreach (var item in restorableSchedules)
                {
                    if (existingSchedules.TryGetValue(item.Id, out var tracked))
                    {
                        tracked.IsDeleted = false;
                        tracked.Name = item.Name;
                    }
                    else
                    {
                        item.IsDeleted = false;
                        db.ClassSchedules.Add(item);
                    }
                    restoredTotal++;
                }
            }

            await auditService.LogAsync(
                request.DryRun ? "SuperAdmin.BackupRestoreDryRun" : "SuperAdmin.BackupRestored",
                nameof(TenantBackupRecord),
                backupId.ToString(),
                new { tenant.Subdomain, request.DryRun, RestoredTotal = restoredTotal, RestorableCounts = restorableCounts },
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);

            return new BackupRestoreResultDto(
                request.DryRun,
                IsValid: true,
                TenantId: tenantId,
                BackupId: backupId,
                FileName: record.FileName,
                ArchiveCounts: manifest.Counts,
                RestorableCounts: restorableCounts,
                RestoredTotal: restoredTotal,
                Warnings: warnings);
        }
    }

    public async Task<PlatformBackupWorkspaceDto> GetPlatformBackupsAsync(CancellationToken cancellationToken = default)
    {
        using (tenantContext.BeginSystemScope())
        {
            var setting = await GetOrCreatePlatformSettingAsync(cancellationToken);
            var history = await db.PlatformBackupRecords
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(r => !r.IsDeleted)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(cancellationToken);

            return new PlatformBackupWorkspaceDto(
                setting.PlatformBackupRetentionDays,
                history.Select(MapPlatformBackupRecord).ToList());
        }
    }

    public async Task<PlatformBackupWorkspaceDto> UpdatePlatformBackupRetentionAsync(
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        if (retentionDays is < 14 or > 30)
        {
            throw new DomainValidationException(
                "INVALID_PLATFORM_RETENTION",
                "Platform database backup retention must be between 14 and 30 days.");
        }

        using (tenantContext.BeginSystemScope())
        {
            var setting = await GetOrCreatePlatformSettingAsync(cancellationToken);
            setting.PlatformBackupRetentionDays = retentionDays;
            setting.UpdatedByUserId = tenantContext.UserId;

            await ApplyPlatformRetentionAsync(retentionDays, DateTimeOffset.UtcNow, null, cancellationToken);

            await auditService.LogAsync(
                "SuperAdmin.PlatformBackupRetentionUpdated",
                nameof(PlatformSetting),
                setting.Id.ToString(),
                new { RetentionDays = retentionDays },
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetPlatformBackupsAsync(cancellationToken);
    }

    public async Task<PlatformBackupRecordDto> RunPlatformBackupAsync(
        BackupTrigger trigger = BackupTrigger.Manual,
        int? retentionDaysOverride = null,
        CancellationToken cancellationToken = default)
    {
        using (tenantContext.BeginSystemScope())
        {
            var setting = await GetOrCreatePlatformSettingAsync(cancellationToken);
            var retentionDays = retentionDaysOverride ?? setting.PlatformBackupRetentionDays;
            if (retentionDays is < 14 or > 30)
            {
                throw new DomainValidationException(
                    "INVALID_PLATFORM_RETENTION",
                    "Platform database backup retention must be between 14 and 30 days.");
            }

            setting.PlatformBackupRetentionDays = retentionDays;
            var now = DateTimeOffset.UtcNow;

            var execResult = await dbBackupExecutor.ExecutePlatformDatabaseBackupAsync(cancellationToken);
            var tenants = await db.Tenants.IgnoreQueryFilters().AsNoTracking().Where(t => !t.IsDeleted).ToListAsync(cancellationToken);
            var plans = await db.Plans.IgnoreQueryFilters().AsNoTracking().Where(p => !p.IsDeleted).ToListAsync(cancellationToken);
            var subscriptions = await db.Subscriptions.IgnoreQueryFilters().AsNoTracking().Where(s => !s.IsDeleted).ToListAsync(cancellationToken);

            byte[] archiveBytes;
            await using (var memory = new MemoryStream())
            {
                using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var descriptor = new
                    {
                        createdAtUtc = now,
                        trigger = trigger.ToString(),
                        databaseProvider = execResult.DatabaseProvider,
                        sqlCommand = execResult.SqlCommandSummary,
                        retentionDays,
                        tenantsCount = tenants.Count,
                        plansCount = plans.Count,
                        subscriptionsCount = subscriptions.Count
                    };
                    await WriteZipTextEntryAsync(archive, "platform-manifest.json", JsonSerializer.Serialize(descriptor, JsonOptions), cancellationToken);
                    await WriteZipTextEntryAsync(archive, "tenants.json", JsonSerializer.Serialize(tenants, JsonOptions), cancellationToken);
                    await WriteZipTextEntryAsync(archive, "plans.json", JsonSerializer.Serialize(plans, JsonOptions), cancellationToken);
                    await WriteZipTextEntryAsync(archive, "subscriptions.json", JsonSerializer.Serialize(subscriptions, JsonOptions), cancellationToken);

                    if (execResult.NativeBackupBytes is { Length: > 0 })
                    {
                        var bakEntry = archive.CreateEntry("admino.bak", CompressionLevel.Fastest);
                        await using var entryStream = bakEntry.Open();
                        await entryStream.WriteAsync(execResult.NativeBackupBytes, cancellationToken);
                    }
                }

                archiveBytes = memory.ToArray();
            }

            var sha256 = Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant();
            var fileName = $"platform-db-backup-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}.bak.zip";
            await using var contentStream = new MemoryStream(archiveBytes);
            var stored = await fileStorage.SaveAsync("platform-backups", fileName, contentStream, cancellationToken);

            var record = new PlatformBackupRecord
            {
                Trigger = trigger,
                Status = BackupStatus.Completed,
                FileName = fileName,
                StorageUrl = stored.Url,
                SizeBytes = stored.SizeBytes,
                DatabaseProvider = execResult.DatabaseProvider,
                SqlCommandSummary = execResult.SqlCommandSummary,
                Sha256 = sha256,
                RetentionDays = retentionDays,
                CompletedAt = now
            };

            db.PlatformBackupRecords.Add(record);
            await db.SaveChangesAsync(cancellationToken);

            await ApplyPlatformRetentionAsync(retentionDays, now, record.Id, cancellationToken);

            await auditService.LogAsync(
                "SuperAdmin.PlatformBackupCompleted",
                nameof(PlatformBackupRecord),
                record.Id.ToString(),
                new { record.FileName, record.SizeBytes, record.RetentionDays, Trigger = trigger.ToString() },
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            return MapPlatformBackupRecord(record);
        }
    }

    public async Task<StoredDownload> DownloadPlatformBackupAsync(
        Guid backupId,
        CancellationToken cancellationToken = default)
    {
        var record = await db.PlatformBackupRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => !r.IsDeleted && r.Id == backupId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(PlatformBackupRecord), backupId.ToString());

        await using var stream = fileStorage.OpenRead(record.StorageUrl)
            ?? throw new EntityNotFoundException("PlatformBackupFile", record.FileName);
        await using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);

        return new StoredDownload(memory.ToArray(), "application/zip", record.FileName);
    }

    public async Task<ReportCleanupWorkspaceDto> GetCleanupWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = await EnsureReportCleanupEnabledAsync(cancellationToken);
        var policy = await GetOrCreateCleanupPolicyAsync(tenantId, cancellationToken);
        var preview = await ComputeCleanupPreviewAsync(tenantId, policy, DateTimeOffset.UtcNow, cancellationToken);
        return new ReportCleanupWorkspaceDto(MapCleanupPolicy(policy), preview);
    }

    public async Task<TenantCleanupPolicyDto> UpdateCleanupPolicyAsync(
        UpdateTenantCleanupPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = await EnsureReportCleanupEnabledAsync(cancellationToken);

        if (request.AuditLogRetentionDays is < 1 or > 3650
            || request.NotificationRetentionDays is < 1 or > 3650
            || request.SoftDeletedRetentionDays is < 1 or > 3650)
        {
            throw new DomainValidationException("INVALID_RETENTION", "Cleanup retention days must be between 1 and 3650.");
        }

        using (tenantContext.BeginSystemScope(tenantId))
        {
            var policy = await GetOrCreateCleanupPolicyAsync(tenantId, cancellationToken);
            policy.AuditLogRetentionDays = request.AuditLogRetentionDays;
            policy.NotificationRetentionDays = request.NotificationRetentionDays;
            policy.SoftDeletedRetentionDays = request.SoftDeletedRetentionDays;
            policy.ArchiveBeforePurge = request.ArchiveBeforePurge;
            policy.IsAutoCleanupEnabled = request.IsAutoCleanupEnabled;

            await auditService.LogAsync(
                "ReportCleanupPolicy.Updated",
                nameof(TenantCleanupPolicy),
                policy.Id.ToString(),
                new
                {
                    policy.AuditLogRetentionDays,
                    policy.NotificationRetentionDays,
                    policy.SoftDeletedRetentionDays,
                    policy.ArchiveBeforePurge,
                    policy.IsAutoCleanupEnabled
                },
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            return MapCleanupPolicy(policy);
        }
    }

    public async Task<ReportCleanupResultDto> RunReportCleanupAsync(
        RunReportCleanupRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = await EnsureReportCleanupEnabledAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        using (tenantContext.BeginSystemScope(tenantId))
        {
            var policy = await GetOrCreateCleanupPolicyAsync(tenantId, cancellationToken);
            var auditCutoff = now.AddDays(-policy.AuditLogRetentionDays);
            var messageCutoff = now.AddDays(-policy.NotificationRetentionDays);
            var softDeleteCutoff = now.AddDays(-policy.SoftDeletedRetentionDays);

            var allAuditLogs = await db.AuditLogs
                .IgnoreQueryFilters()
                .Where(a => !a.IsDeleted && a.TenantId == tenantId)
                .ToListAsync(cancellationToken);
            var oldAuditLogs = allAuditLogs.Where(a => a.CreatedAt < auditCutoff).ToList();

            var allMessages = await db.NotificationOutboxes
                .IgnoreQueryFilters()
                .Where(m => !m.IsDeleted && m.TenantId == tenantId && m.Status != NotificationStatus.Queued)
                .ToListAsync(cancellationToken);
            var oldMessages = allMessages.Where(m => m.CreatedAt < messageCutoff).ToList();

            var softDeletedMarks = (await db.AttendanceMarks.IgnoreQueryFilters().Where(x => x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken))
                .Where(x => (x.UpdatedAt ?? x.CreatedAt) < softDeleteCutoff).ToList();
            var softDeletedEnrollments = (await db.StudentEnrollments.IgnoreQueryFilters().Where(x => x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken))
                .Where(x => (x.UpdatedAt ?? x.CreatedAt) < softDeleteCutoff).ToList();
            var softDeletedStudents = (await db.Students.IgnoreQueryFilters().Where(x => x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken))
                .Where(x => (x.UpdatedAt ?? x.CreatedAt) < softDeleteCutoff).ToList();
            var softDeletedSchedules = (await db.ClassSchedules.IgnoreQueryFilters().Where(x => x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken))
                .Where(x => (x.UpdatedAt ?? x.CreatedAt) < softDeleteCutoff).ToList();
            var softDeletedSubjects = (await db.Subjects.IgnoreQueryFilters().Where(x => x.IsDeleted && x.TenantId == tenantId).ToListAsync(cancellationToken))
                .Where(x => (x.UpdatedAt ?? x.CreatedAt) < softDeleteCutoff).ToList();

            var softDeletedTotal = softDeletedMarks.Count
                + softDeletedEnrollments.Count
                + softDeletedStudents.Count
                + softDeletedSchedules.Count
                + softDeletedSubjects.Count;

            var totalEligible = oldAuditLogs.Count + oldMessages.Count + softDeletedTotal;

            if (request.DryRun)
            {
                return new ReportCleanupResultDto(
                    DryRun: true,
                    PurgedAuditLogs: oldAuditLogs.Count,
                    PurgedMessages: oldMessages.Count,
                    PurgedSoftDeletedRecords: softDeletedTotal,
                    TotalPurged: totalEligible,
                    ArchiveUrl: null,
                    ExecutedAt: now);
            }

            string? archiveUrl = null;
            if (policy.ArchiveBeforePurge && totalEligible > 0)
            {
                await using var memory = new MemoryStream();
                using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
                {
                    await WriteZipTextEntryAsync(archive, "purged-audit-logs.json", JsonSerializer.Serialize(oldAuditLogs, JsonOptions), cancellationToken);
                    await WriteZipTextEntryAsync(archive, "purged-messages.json", JsonSerializer.Serialize(oldMessages, JsonOptions), cancellationToken);
                    await WriteZipTextEntryAsync(
                        archive,
                        "purged-soft-deleted.json",
                        JsonSerializer.Serialize(new
                        {
                            attendanceMarks = softDeletedMarks,
                            enrollments = softDeletedEnrollments,
                            students = softDeletedStudents,
                            classSchedules = softDeletedSchedules,
                            subjects = softDeletedSubjects
                        }, JsonOptions),
                        cancellationToken);
                }

                memory.Position = 0;
                var fileName = $"cleanup-archive-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}.zip";
                var stored = await fileStorage.SaveAsync($"cleanup-archives/{tenantId:N}", fileName, memory, cancellationToken);
                archiveUrl = stored.Url;
            }

            if (totalEligible > 0)
            {
                db.AttendanceMarks.RemoveRange(softDeletedMarks);
                db.StudentEnrollments.RemoveRange(softDeletedEnrollments);
                db.ClassSchedules.RemoveRange(softDeletedSchedules);
                db.Students.RemoveRange(softDeletedStudents);
                db.Subjects.RemoveRange(softDeletedSubjects);
                db.NotificationOutboxes.RemoveRange(oldMessages);
                db.AuditLogs.RemoveRange(oldAuditLogs);

                await db.SaveChangesHardDeleteAsync(cancellationToken);
            }

            policy.LastRunAt = now;
            policy.LastPurgedAuditLogs = oldAuditLogs.Count;
            policy.LastPurgedMessages = oldMessages.Count;
            policy.LastPurgedSoftDeleted = softDeletedTotal;
            policy.LastArchiveUrl = archiveUrl ?? policy.LastArchiveUrl;

            await auditService.LogAsync(
                "ReportCleanup.Executed",
                nameof(TenantCleanupPolicy),
                policy.Id.ToString(),
                new
                {
                    PurgedAuditLogs = oldAuditLogs.Count,
                    PurgedMessages = oldMessages.Count,
                    PurgedSoftDeleted = softDeletedTotal,
                    ArchiveUrl = archiveUrl
                },
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);

            return new ReportCleanupResultDto(
                DryRun: false,
                PurgedAuditLogs: oldAuditLogs.Count,
                PurgedMessages: oldMessages.Count,
                PurgedSoftDeletedRecords: softDeletedTotal,
                TotalPurged: totalEligible,
                ArchiveUrl: archiveUrl,
                ExecutedAt: now);
        }
    }

    private async Task<ReportCleanupPreviewDto> ComputeCleanupPreviewAsync(
        Guid tenantId,
        TenantCleanupPolicy policy,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var auditCutoff = now.AddDays(-policy.AuditLogRetentionDays);
        var messageCutoff = now.AddDays(-policy.NotificationRetentionDays);
        var softDeleteCutoff = now.AddDays(-policy.SoftDeletedRetentionDays);

        var auditCount = (await db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(a => !a.IsDeleted && a.TenantId == tenantId)
            .Select(a => a.CreatedAt)
            .ToListAsync(cancellationToken))
            .Count(ts => ts < auditCutoff);

        var messageCount = (await db.NotificationOutboxes.IgnoreQueryFilters().AsNoTracking()
            .Where(m => !m.IsDeleted && m.TenantId == tenantId && m.Status != NotificationStatus.Queued)
            .Select(m => m.CreatedAt)
            .ToListAsync(cancellationToken))
            .Count(ts => ts < messageCutoff);

        var sdStudents = (await db.Students.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.IsDeleted && x.TenantId == tenantId)
            .Select(x => new { x.UpdatedAt, x.CreatedAt })
            .ToListAsync(cancellationToken))
            .Count(x => (x.UpdatedAt ?? x.CreatedAt) < softDeleteCutoff);

        var sdEnrollments = (await db.StudentEnrollments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.IsDeleted && x.TenantId == tenantId)
            .Select(x => new { x.UpdatedAt, x.CreatedAt })
            .ToListAsync(cancellationToken))
            .Count(x => (x.UpdatedAt ?? x.CreatedAt) < softDeleteCutoff);

        var sdMarks = (await db.AttendanceMarks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.IsDeleted && x.TenantId == tenantId)
            .Select(x => new { x.UpdatedAt, x.CreatedAt })
            .ToListAsync(cancellationToken))
            .Count(x => (x.UpdatedAt ?? x.CreatedAt) < softDeleteCutoff);

        var sdSchedules = (await db.ClassSchedules.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.IsDeleted && x.TenantId == tenantId)
            .Select(x => new { x.UpdatedAt, x.CreatedAt })
            .ToListAsync(cancellationToken))
            .Count(x => (x.UpdatedAt ?? x.CreatedAt) < softDeleteCutoff);

        var sdSubjects = (await db.Subjects.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.IsDeleted && x.TenantId == tenantId)
            .Select(x => new { x.UpdatedAt, x.CreatedAt })
            .ToListAsync(cancellationToken))
            .Count(x => (x.UpdatedAt ?? x.CreatedAt) < softDeleteCutoff);

        var softDeletedCount = sdStudents + sdEnrollments + sdMarks + sdSchedules + sdSubjects;

        return new ReportCleanupPreviewDto(
            auditCount,
            messageCount,
            softDeletedCount,
            auditCount + messageCount + softDeletedCount);
    }

    private async Task ApplyTenantRetentionAsync(
        Guid tenantId,
        TenantBackupPolicy policy,
        DateTimeOffset now,
        Guid? keepBackupId,
        CancellationToken cancellationToken)
    {
        var backups = await db.TenantBackupRecords
            .IgnoreQueryFilters()
            .Where(r => !r.IsDeleted && r.TenantId == tenantId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        var cutoff = now.AddDays(-policy.RetentionDays);
        for (var i = 0; i < backups.Count; i++)
        {
            var b = backups[i];
            if (keepBackupId.HasValue && b.Id == keepBackupId.Value)
            {
                continue;
            }

            if (i >= policy.MaxBackupsToKeep || b.CreatedAt < cutoff)
            {
                await fileStorage.DeleteAsync(b.StorageUrl, cancellationToken);
                db.TenantBackupRecords.Remove(b);
            }
        }
    }

    private async Task ApplyPlatformRetentionAsync(
        int retentionDays,
        DateTimeOffset now,
        Guid? keepBackupId,
        CancellationToken cancellationToken)
    {
        var backups = await db.PlatformBackupRecords
            .IgnoreQueryFilters()
            .Where(r => !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        var cutoff = now.AddDays(-retentionDays);
        foreach (var b in backups)
        {
            if (keepBackupId.HasValue && b.Id == keepBackupId.Value)
            {
                continue;
            }

            if (b.CreatedAt < cutoff)
            {
                await fileStorage.DeleteAsync(b.StorageUrl, cancellationToken);
                db.PlatformBackupRecords.Remove(b);
            }
        }
    }

    private async Task<TenantBackupPolicy> GetOrCreateBackupPolicyAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var policy = await db.TenantBackupPolicies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => !p.IsDeleted && p.TenantId == tenantId, cancellationToken);

        if (policy is null)
        {
            policy = new TenantBackupPolicy
            {
                TenantId = tenantId,
                IsScheduledEnabled = true,
                RetentionDays = 30,
                MaxBackupsToKeep = 10,
                PreferredHourUtc = 20
            };
            using (tenantContext.BeginSystemScope(tenantId))
            {
                db.TenantBackupPolicies.Add(policy);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return policy;
    }

    private async Task<TenantCleanupPolicy> GetOrCreateCleanupPolicyAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var policy = await db.TenantCleanupPolicies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => !p.IsDeleted && p.TenantId == tenantId, cancellationToken);

        if (policy is null)
        {
            policy = new TenantCleanupPolicy
            {
                TenantId = tenantId,
                AuditLogRetentionDays = 90,
                NotificationRetentionDays = 90,
                SoftDeletedRetentionDays = 30,
                ArchiveBeforePurge = true,
                IsAutoCleanupEnabled = false
            };
            using (tenantContext.BeginSystemScope(tenantId))
            {
                db.TenantCleanupPolicies.Add(policy);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return policy;
    }

    private async Task<PlatformSetting> GetOrCreatePlatformSettingAsync(CancellationToken cancellationToken)
    {
        var setting = await db.PlatformSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => !s.IsDeleted && s.Key == "default", cancellationToken);

        if (setting is null)
        {
            setting = new PlatformSetting
            {
                Key = "default",
                IsMaintenanceMode = false,
                PlatformBackupRetentionDays = 14
            };
            db.PlatformSettings.Add(setting);
            await db.SaveChangesAsync(cancellationToken);
        }

        return setting;
    }

    private async Task<Guid> EnsureAutoBackupEnabledAsync(CancellationToken cancellationToken)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var eval = await featureService.EvaluateAsync(tenantId, FeatureKey.AutoBackup, cancellationToken);
        if (!eval.IsInPlan)
        {
            throw new FeatureNotInPlanException(FeatureKey.AutoBackup);
        }

        if (!eval.IsEnabled)
        {
            throw new FeatureDisabledException(FeatureKey.AutoBackup);
        }

        return tenantId;
    }

    private async Task<Guid> EnsureReportCleanupEnabledAsync(CancellationToken cancellationToken)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var eval = await featureService.EvaluateAsync(tenantId, FeatureKey.ReportCleanup, cancellationToken);
        if (!eval.IsInPlan)
        {
            throw new FeatureNotInPlanException(FeatureKey.ReportCleanup);
        }

        if (!eval.IsEnabled)
        {
            throw new FeatureDisabledException(FeatureKey.ReportCleanup);
        }

        return tenantId;
    }

    private static TenantBackupPolicyDto MapBackupPolicy(TenantBackupPolicy policy) =>
        new(policy.IsScheduledEnabled, policy.RetentionDays, policy.MaxBackupsToKeep, policy.PreferredHourUtc);

    private static TenantCleanupPolicyDto MapCleanupPolicy(TenantCleanupPolicy policy) =>
        new(
            policy.AuditLogRetentionDays,
            policy.NotificationRetentionDays,
            policy.SoftDeletedRetentionDays,
            policy.ArchiveBeforePurge,
            policy.IsAutoCleanupEnabled,
            policy.LastRunAt,
            policy.LastPurgedAuditLogs,
            policy.LastPurgedMessages,
            policy.LastPurgedSoftDeleted,
            policy.LastArchiveUrl);

    private static TenantBackupRecordDto MapTenantBackupRecord(TenantBackupRecord record)
    {
        IReadOnlyDictionary<string, int> counts;
        try
        {
            counts = JsonSerializer.Deserialize<Dictionary<string, int>>(record.SummaryJson) ?? new Dictionary<string, int>();
        }
        catch
        {
            counts = new Dictionary<string, int>();
        }

        return new TenantBackupRecordDto(
            record.Id,
            record.TenantId,
            record.Trigger.ToString(),
            record.Status.ToString(),
            record.FileName,
            record.StorageUrl,
            record.SizeBytes,
            record.Sha256,
            record.TotalRecords,
            record.UploadedFilesCount,
            counts,
            record.ErrorMessage,
            record.CreatedAt,
            record.CompletedAt);
    }

    private static PlatformBackupRecordDto MapPlatformBackupRecord(PlatformBackupRecord record) =>
        new(
            record.Id,
            record.Trigger.ToString(),
            record.Status.ToString(),
            record.FileName,
            record.StorageUrl,
            record.SizeBytes,
            record.DatabaseProvider,
            record.SqlCommandSummary,
            record.Sha256,
            record.RetentionDays,
            record.ErrorMessage,
            record.CreatedAt,
            record.CompletedAt);

    private static async Task WriteZipTextEntryAsync(
        ZipArchive archive,
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        await stream.WriteAsync(bytes, cancellationToken);
    }

    private static string BuildCsv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }
}
