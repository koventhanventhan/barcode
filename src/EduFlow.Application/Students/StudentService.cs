using EduFlow.Application.Academics;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Common.Security;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Students;

public interface IStudentService
{
    Task<StudentPageDto> ListAsync(StudentListQuery query, CancellationToken cancellationToken = default);
    Task<StudentDto> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<StudentDto> CreateAsync(UpsertStudentRequest request, CancellationToken cancellationToken = default);
    Task<StudentDto> UpdateAsync(Guid id, UpsertStudentRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<StudentDto> SavePhotoAsync(Guid id, Stream content, string fileName, string? contentType, long length, CancellationToken cancellationToken = default);
    Task<StudentImportReport> ImportAsync(Stream content, string fileName, CancellationToken cancellationToken = default);
    Task<StoredDownload> TemplateAsync(string? format, CancellationToken cancellationToken = default);
    Task<StoredDownload> ExportAsync(string? format, CancellationToken cancellationToken = default);
    Task<StoredDownload> IdCardAsync(Guid id, CancellationToken cancellationToken = default);
    Task<StoredDownload> IdCardsAsync(BulkStudentRequest request, CancellationToken cancellationToken = default);
    Task<StoredDownload> LabelsAsync(BulkStudentRequest request, CancellationToken cancellationToken = default);
}

public sealed class StudentService(
    IAppDbContext db,
    ITenantContext tenantContext,
    ILimitService limitService,
    IFeatureService featureService,
    IAuditService auditService,
    IAcademicDocumentService documents,
    IBarcodeEncoder barcodes,
    IWorkbookFile workbook,
    IFileStorage files,
    IAntivirusScanner? antivirusScanner = null) : IStudentService
{
    private const int MaxImportRows = 2000;
    private const int MaxPrint = 500;
    private const long MaxPhotoBytes = 2 * 1024 * 1024;

    public async Task<StudentPageDto> ListAsync(StudentListQuery query, CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 50 : query.PageSize;
        var students = Filtered(query);
        var total = await students.CountAsync(cancellationToken);
        var pageItems = await students
            .OrderBy(student => student.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var items = await MapListAsync(pageItems, cancellationToken);
        return new StudentPageDto(items, total, page, pageSize);
    }

    public async Task<StudentDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Student), id.ToString());
        return await MapStudentAsync(student, cancellationToken);
    }

    public async Task<StudentDto> CreateAsync(UpsertStudentRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        await limitService.EnsureWithinLimitAsync(tenantId, LimitKey.Students, cancellationToken: cancellationToken);
        var level = await RequireEnabledLevelAsync(request.EducationLevelId, cancellationToken);
        await RequireBranchAsync(request.BranchId, cancellationToken);
        var profile = ParseProfile(request);
        var student = new Student
        {
            StudentCode = await AllocateCodeAsync(tenantId, cancellationToken),
            EducationLevelId = level.Id,
            BranchId = request.BranchId
        };
        Apply(student, profile);
        db.Students.Add(student);
        await SyncEnrollmentsAsync(student, request.BatchIds ?? [], cancellationToken);
        await auditService.LogAsync("Student.Created", nameof(Student), student.Id.ToString(), new { student.StudentCode }, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await MapStudentAsync(student, cancellationToken);
    }

    public async Task<StudentDto> UpdateAsync(Guid id, UpsertStudentRequest request, CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var student = await db.Students.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Student), id.ToString());
        var level = await RequireEnabledLevelAsync(request.EducationLevelId, cancellationToken);
        await RequireBranchAsync(request.BranchId, cancellationToken);
        var profile = ParseProfile(request);
        student.EducationLevelId = level.Id;
        student.BranchId = request.BranchId;
        Apply(student, profile);
        if (request.BatchIds is not null)
        {
            await SyncEnrollmentsAsync(student, request.BatchIds, cancellationToken);
        }

        await auditService.LogAsync("Student.Updated", nameof(Student), student.Id.ToString(), new { student.StudentCode }, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await MapStudentAsync(student, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var student = await db.Students.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Student), id.ToString());
        var enrollments = await db.StudentEnrollments.Where(item => item.StudentId == id).ToListAsync(cancellationToken);
        foreach (var enrollment in enrollments)
        {
            db.StudentEnrollments.Remove(enrollment);
        }

        db.Students.Remove(student);
        await auditService.LogAsync("Student.Deleted", nameof(Student), student.Id.ToString(), new { student.StudentCode }, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<StudentDto> SavePhotoAsync(
        Guid id,
        Stream content,
        string fileName,
        string? contentType,
        long length,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        if (length <= 0 || length > MaxPhotoBytes)
        {
            throw new DomainValidationException("PHOTO_TOO_LARGE", "Student photos must be 2 MB or smaller.");
        }

        var extension = PhotoExtension(fileName, contentType);
        var student = await db.Students.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Student), id.ToString());

        var check = await limitService.CheckAsync(tenantId, LimitKey.CloudStorageGb, increment: 0, cancellationToken: cancellationToken);
        if (check.MaxAllowed is int gigabytes)
        {
            var used = await db.Students.IgnoreQueryFilters()
                .Where(item => !item.IsDeleted && item.TenantId == tenantId)
                .SumAsync(item => (long?)item.PhotoBytes, cancellationToken) ?? 0;
            var maxBytes = gigabytes * 1024L * 1024L * 1024L;
            if (used - student.PhotoBytes + length > maxBytes)
            {
                throw new PlanLimitReachedException(LimitKey.CloudStorageGb, gigabytes);
            }
        }

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        await FileUploadValidator.EnsureCleanAndValidSignatureAsync(
            buffer,
            fileName,
            contentType,
            extension,
            "PHOTO_TYPE",
            "Upload a JPEG, PNG, or WebP photo.",
            antivirusScanner,
            cancellationToken);

        buffer.Position = 0;
        var stored = await files.SaveAsync($"students/{tenantId:N}", $"{Guid.NewGuid():N}{extension}", buffer, cancellationToken);
        student.PhotoUrl = stored.Url;
        student.PhotoBytes = stored.SizeBytes;
        await auditService.LogAsync("Student.PhotoUploaded", nameof(Student), student.Id.ToString(), new { student.StudentCode, stored.SizeBytes }, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await MapStudentAsync(student, cancellationToken);
    }

    public async Task<StudentImportReport> ImportAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
        {
            throw new DomainValidationException("IMPORT_EMPTY", "The file is empty.");
        }

        if (buffer.Length > 5 * 1024 * 1024)
        {
            throw new DomainValidationException("IMPORT_FILE", "Import files must be 5 MB or smaller.");
        }

        buffer.Position = 0;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".csv" or ".xlsx"))
        {
            throw new DomainValidationException("IMPORT_FILE", "Upload a .csv or .xlsx file.");
        }

        await FileUploadValidator.EnsureCleanAndValidSignatureAsync(
            buffer,
            fileName,
            null,
            extension,
            "IMPORT_FILE",
            "Upload a valid .csv or .xlsx file.",
            antivirusScanner,
            cancellationToken);

        buffer.Position = 0;
        IReadOnlyList<TabularRow> rows = extension switch
        {
            ".csv" => CsvTable.Read(buffer),
            ".xlsx" => workbook.Read(buffer),
            _ => throw new DomainValidationException("IMPORT_FILE", "Upload a .csv or .xlsx file.")
        };

        rows = rows.Select(row => new TabularRow(
            row.RowNumber,
            row.Cells.ToDictionary(pair => ImportColumns.Normalize(pair.Key), pair => pair.Value.Trim(), StringComparer.OrdinalIgnoreCase))).ToList();

        if (rows.Count == 0)
        {
            throw new DomainValidationException("IMPORT_EMPTY", "Add at least one student row under the header.");
        }

        if (rows.Count > MaxImportRows)
        {
            throw new DomainValidationException("IMPORT_FILE", $"Import up to {MaxImportRows} rows at a time.");
        }

        var missing = ImportColumns.Missing(rows);
        if (missing.Count > 0)
        {
            throw new DomainValidationException("IMPORT_COLUMNS", $"The file is missing columns: {string.Join(", ", missing)}.");
        }

        var levels = await db.TenantEducationLevels.AsNoTracking()
            .Where(row => row.IsEnabled)
            .Join(db.EducationLevels.AsNoTracking(), row => row.EducationLevelId, level => level.Id, (_, level) => level)
            .ToDictionaryAsync(level => level.Code, level => level.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var batches = await db.AcademicBatches.AsNoTracking()
            .ToDictionaryAsync(batch => batch.Code, batch => (batch.Id, batch.EducationLevelId), StringComparer.OrdinalIgnoreCase, cancellationToken);
        var identities = await db.Students.AsNoTracking()
            .Select(student => ContactRules.Identity(student.FullName, student.GuardianPhone))
            .ToListAsync(cancellationToken);
        var catalog = new ImportCatalog(levels, batches, identities.ToHashSet(StringComparer.Ordinal));
        var (valid, errors) = StudentImportRules.Validate(rows, catalog, ColomboClock.Today());
        if (valid.Count > 0)
        {
            await limitService.EnsureWithinLimitAsync(tenantId, LimitKey.Students, increment: valid.Count, cancellationToken: cancellationToken);
            var next = StudentCodes.Next(await db.Students.IgnoreQueryFilters()
                .Where(student => student.TenantId == tenantId)
                .Select(student => student.StudentCode)
                .ToListAsync(cancellationToken));
            foreach (var row in valid)
            {
                var student = new Student
                {
                    StudentCode = StudentCodes.Format(next++),
                    FullName = row.FullName,
                    GuardianName = row.GuardianName,
                    GuardianPhone = row.GuardianPhone,
                    GuardianPhoneSecondary = row.GuardianPhoneSecondary,
                    Address = row.Address,
                    City = row.City,
                    EducationLevelId = row.EducationLevelId,
                    Gender = Enum.Parse<StudentGender>(row.Gender, true),
                    DateOfBirth = row.DateOfBirth,
                    AdmissionDate = row.AdmissionDate,
                    Status = Enum.Parse<StudentStatus>(row.Status, true)
                };
                db.Students.Add(student);
                await SyncEnrollmentsAsync(student, row.BatchIds, cancellationToken);
            }

            await auditService.LogAsync("Students.Imported", nameof(Student), null, new { Imported = valid.Count, Failed = errors.Count }, cancellationToken: cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return new StudentImportReport(valid.Count, errors.Count, errors);
    }

    public Task<StoredDownload> TemplateAsync(string? format, CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var headers = ImportColumns.TemplateHeaders;
        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new StoredDownload(CsvTable.Write(headers, []), "text/csv", "student-import.csv"));
        }

        return Task.FromResult(new StoredDownload(
            workbook.Write("Students", headers, []),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "student-import.xlsx"));
    }

    public async Task<StoredDownload> ExportAsync(string? format, CancellationToken cancellationToken = default)
    {
        await EnsureFeatureAsync(FeatureKey.DataExport, cancellationToken);
        var students = await db.Students.AsNoTracking().OrderBy(student => student.FullName).ToListAsync(cancellationToken);
        var list = await MapListAsync(students, cancellationToken);
        var headers = new[] { "studentCode", "fullName", "guardianName", "guardianPhone", "level", "batches", "status" };
        var rows = list.Select(item => (IReadOnlyList<string>)
        [
            item.StudentCode,
            item.FullName,
            item.GuardianName,
            item.GuardianPhone,
            item.EducationLevelName,
            item.BatchNames,
            item.Status
        ]).ToList();

        if (string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return new StoredDownload(workbook.Write("Students", headers, rows), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "students.xlsx");
        }

        if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
        {
            var branding = await LoadBrandingAsync(cancellationToken);
            var directory = list.Select(item => new DirectoryRow(item.StudentCode, item.FullName, item.GuardianPhone, item.EducationLevelName, item.Status)).ToList();
            var pdf = documents.BuildStudentDirectory(branding.Name, branding.Primary, directory);
            return new StoredDownload(pdf, "application/pdf", "students.pdf");
        }

        return new StoredDownload(CsvTable.Write(headers, rows), "text/csv", "students.csv");
    }

    public async Task<StoredDownload> IdCardAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureFeatureAsync(FeatureKey.BarcodeGenerator, cancellationToken);
        var cards = await BuildCardsAsync([id], cancellationToken);
        return new StoredDownload(documents.BuildIdCards(cards, eightUp: false), "application/pdf", $"{cards[0].StudentCode}-id-card.pdf");
    }

    public async Task<StoredDownload> IdCardsAsync(BulkStudentRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureFeatureAsync(FeatureKey.BarcodeGenerator, cancellationToken);
        var ids = await ResolveIdsAsync(request, cancellationToken);
        var cards = await BuildCardsAsync(ids, cancellationToken);
        return new StoredDownload(documents.BuildIdCards(cards, eightUp: true), "application/pdf", "id-cards.pdf");
    }

    public async Task<StoredDownload> LabelsAsync(BulkStudentRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureFeatureAsync(FeatureKey.BarcodeGenerator, cancellationToken);
        var ids = await ResolveIdsAsync(request, cancellationToken);
        var cards = await BuildCardsAsync(ids, cancellationToken);
        return new StoredDownload(documents.BuildLabels(cards), "application/pdf", "barcode-labels.pdf");
    }

    private IQueryable<Student> Filtered(StudentListQuery query)
    {
        var students = db.Students.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            students = students.Where(student =>
                student.FullName.Contains(term) ||
                student.StudentCode.Contains(term) ||
                student.GuardianName.Contains(term) ||
                student.GuardianPhone.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<StudentStatus>(query.Status, true, out var status))
            {
                throw new DomainValidationException("INVALID_STATUS", "Status must be Active, Inactive, or Left.");
            }

            students = students.Where(student => student.Status == status);
        }

        if (query.EducationLevelId.HasValue)
        {
            students = students.Where(student => student.EducationLevelId == query.EducationLevelId.Value);
        }

        if (query.BatchId.HasValue)
        {
            var batchId = query.BatchId.Value;
            var enrolled = db.StudentEnrollments.Where(item => item.BatchId == batchId && item.IsActive).Select(item => item.StudentId);
            students = students.Where(student => enrolled.Contains(student.Id));
        }

        return students;
    }

    private async Task<IReadOnlyList<Guid>> ResolveIdsAsync(BulkStudentRequest request, CancellationToken cancellationToken)
    {
        if (request.All)
        {
            var ids = await db.Students.Where(student => student.Status == StudentStatus.Active).Select(student => student.Id).ToListAsync(cancellationToken);
            return Cap(ids);
        }

        var selected = request.StudentIds?.Distinct().ToList() ?? [];
        if (selected.Count == 0)
        {
            throw new DomainValidationException("NO_STUDENTS", "Select at least one student.");
        }

        return Cap(selected);
    }

    private static IReadOnlyList<Guid> Cap(IReadOnlyList<Guid> ids)
    {
        if (ids.Count > MaxPrint)
        {
            throw new DomainValidationException("TOO_MANY_STUDENTS", $"Print up to {MaxPrint} students at a time.");
        }

        return ids;
    }

    private async Task<IReadOnlyList<IdCardContent>> BuildCardsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var students = await db.Students.AsNoTracking().Where(student => ids.Contains(student.Id)).ToListAsync(cancellationToken);
        if (students.Count != ids.Distinct().Count())
        {
            throw new EntityNotFoundException(nameof(Student));
        }

        var branding = await LoadBrandingAsync(cancellationToken);
        var list = await MapListAsync(students, cancellationToken);
        var ordered = ids.Select(id => list.Single(item => item.Id == id)).ToList();
        var cards = new List<IdCardContent>();
        foreach (var item in ordered)
        {
            byte[]? photo = null;
            if (!string.IsNullOrWhiteSpace(item.PhotoUrl))
            {
                var opened = files.OpenRead(item.PhotoUrl);
                if (opened is not null)
                {
                    await using (opened)
                    {
                        using var memory = new MemoryStream();
                        await opened.CopyToAsync(memory, cancellationToken);
                        var bytes = memory.ToArray();
                        var jpeg = bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8;
                        var png = bytes.Length > 4 && bytes[0] == 0x89 && bytes[1] == 0x50;
                        if (jpeg || png)
                        {
                            photo = bytes;
                        }
                    }
                }
            }

            cards.Add(new IdCardContent(
                branding.Name,
                branding.Primary,
                branding.Logo,
                photo,
                item.FullName,
                item.StudentCode,
                item.EducationLevelName,
                string.IsNullOrWhiteSpace(item.BatchNames) ? "-" : item.BatchNames,
                barcodes.EncodeCode128(item.StudentCode)));
        }

        return cards;
    }

    private async Task<(string Name, string Primary, byte[]? Logo)> LoadBrandingAsync(CancellationToken cancellationToken)
    {
        var profile = await db.BrandingProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(item => item.Id == tenantContext.TenantId, cancellationToken);
        var name = string.IsNullOrWhiteSpace(profile?.InstituteDisplayName) ? tenant.Name : profile!.InstituteDisplayName;
        var primary = string.IsNullOrWhiteSpace(profile?.PrimaryColor) ? BrandingProfile.DefaultPrimaryColor : profile!.PrimaryColor;
        byte[]? logo = null;
        if (!string.IsNullOrWhiteSpace(profile?.LogoUrl))
        {
            var opened = files.OpenRead(profile.LogoUrl);
            if (opened is not null)
            {
                await using (opened)
                {
                    using var memory = new MemoryStream();
                    await opened.CopyToAsync(memory, cancellationToken);
                    logo = memory.ToArray();
                }
            }
        }

        return (name, primary, logo);
    }

    private async Task<string> AllocateCodeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var codes = await db.Students.IgnoreQueryFilters()
            .Where(student => student.TenantId == tenantId)
            .Select(student => student.StudentCode)
            .ToListAsync(cancellationToken);
        return StudentCodes.Format(StudentCodes.Next(codes));
    }

    private async Task EnsureFeatureAsync(FeatureKey key, CancellationToken cancellationToken)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var evaluation = await featureService.EvaluateAsync(tenantId, key, cancellationToken);
        if (!evaluation.IsInPlan)
        {
            throw new FeatureNotInPlanException(key);
        }

        if (!evaluation.IsEnabled)
        {
            throw new FeatureDisabledException(key);
        }
    }

    private async Task<EducationLevel> RequireEnabledLevelAsync(Guid levelId, CancellationToken cancellationToken)
    {
        var level = await db.EducationLevels.FirstOrDefaultAsync(item => item.Id == levelId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(EducationLevel), levelId.ToString());
        var enabled = await db.TenantEducationLevels.AnyAsync(row => row.EducationLevelId == levelId && row.IsEnabled, cancellationToken);
        if (!enabled)
        {
            throw new DomainValidationException("LEVEL_NOT_ENABLED", $"{level.Name} is not enabled for this institute.");
        }

        return level;
    }

    private async Task RequireBranchAsync(Guid? branchId, CancellationToken cancellationToken)
    {
        if (!branchId.HasValue)
        {
            return;
        }

        var exists = await db.Branches.AnyAsync(branch => branch.Id == branchId.Value, cancellationToken);
        if (!exists)
        {
            throw new EntityNotFoundException(nameof(Branch), branchId.Value.ToString());
        }
    }

    private async Task SyncEnrollmentsAsync(Student student, IReadOnlyList<Guid> batchIds, CancellationToken cancellationToken)
    {
        var distinct = batchIds.Distinct().ToList();
        if (distinct.Count > 0)
        {
            var batches = await db.AcademicBatches.Where(batch => distinct.Contains(batch.Id)).ToListAsync(cancellationToken);
            if (batches.Count != distinct.Count)
            {
                throw new EntityNotFoundException("Batch");
            }

            if (batches.Any(batch => batch.EducationLevelId != student.EducationLevelId))
            {
                throw new DomainValidationException("BATCH_LEVEL_MISMATCH", "Each batch must belong to the student's education level.");
            }
        }

        var tenantId = student.TenantId == Guid.Empty ? TenantGuard.Require(tenantContext) : student.TenantId;
        var existing = await db.StudentEnrollments.IgnoreQueryFilters()
            .Where(item => !item.IsDeleted || item.StudentId == student.Id)
            .Where(item => item.TenantId == tenantId && item.StudentId == student.Id)
            .ToListAsync(cancellationToken);

        foreach (var row in existing.Where(item => !item.IsDeleted && !distinct.Contains(item.BatchId)))
        {
            row.IsActive = false;
            row.IsDeleted = true;
        }

        var today = ColomboClock.Today();
        foreach (var batchId in distinct)
        {
            var row = existing.FirstOrDefault(item => item.BatchId == batchId);
            if (row is null)
            {
                db.StudentEnrollments.Add(new StudentEnrollment
                {
                    TenantId = tenantId,
                    StudentId = student.Id,
                    BatchId = batchId,
                    EnrolledOn = today,
                    IsActive = true
                });
            }
            else
            {
                row.IsDeleted = false;
                row.IsActive = true;
            }
        }
    }

    private async Task<IReadOnlyList<StudentListItemDto>> MapListAsync(IReadOnlyList<Student> students, CancellationToken cancellationToken)
    {
        var levelIds = students.Select(student => student.EducationLevelId).Distinct().ToList();
        var levels = await db.EducationLevels.AsNoTracking()
            .Where(level => levelIds.Contains(level.Id))
            .ToDictionaryAsync(level => level.Id, cancellationToken);
        var studentIds = students.Select(student => student.Id).ToList();
        var enrollments = await db.StudentEnrollments.AsNoTracking()
            .Where(item => studentIds.Contains(item.StudentId) && item.IsActive)
            .ToListAsync(cancellationToken);
        var batchIds = enrollments.Select(item => item.BatchId).Distinct().ToList();
        var batches = await db.AcademicBatches.AsNoTracking()
            .Where(batch => batchIds.Contains(batch.Id))
            .ToDictionaryAsync(batch => batch.Id, cancellationToken);

        return students.Select(student =>
        {
            var names = enrollments
                .Where(item => item.StudentId == student.Id)
                .Select(item => batches.TryGetValue(item.BatchId, out var batch) ? batch.Name : null)
                .Where(name => name is not null)
                .ToList();
            levels.TryGetValue(student.EducationLevelId, out var level);
            return new StudentListItemDto(
                student.Id,
                student.StudentCode,
                student.FullName,
                student.GuardianName,
                student.GuardianPhone,
                student.EducationLevelId,
                level?.Name ?? string.Empty,
                student.Status.ToString(),
                string.Join(", ", names),
                student.PhotoUrl);
        }).ToList();
    }

    private async Task<StudentDto> MapStudentAsync(Student student, CancellationToken cancellationToken)
    {
        var level = await db.EducationLevels.AsNoTracking().FirstOrDefaultAsync(item => item.Id == student.EducationLevelId, cancellationToken);
        var enrollments = await db.StudentEnrollments.AsNoTracking()
            .Where(item => item.StudentId == student.Id && item.IsActive)
            .ToListAsync(cancellationToken);
        var batchIds = enrollments.Select(item => item.BatchId).ToList();
        var batches = await db.AcademicBatches.AsNoTracking()
            .Where(batch => batchIds.Contains(batch.Id))
            .ToDictionaryAsync(batch => batch.Id, cancellationToken);
        var mapped = enrollments.Select(item =>
        {
            batches.TryGetValue(item.BatchId, out var batch);
            return new StudentEnrollmentDto(item.Id, item.BatchId, batch?.Name ?? string.Empty, batch?.Code ?? string.Empty, item.IsActive);
        }).ToList();

        return new StudentDto(
            student.Id,
            student.StudentCode,
            student.FullName,
            student.DateOfBirth,
            student.Gender.ToString(),
            student.PhotoUrl,
            student.GuardianName,
            student.GuardianPhone,
            student.GuardianPhoneSecondary,
            student.Address,
            student.City,
            student.EducationLevelId,
            level?.Name ?? string.Empty,
            student.BranchId,
            student.Status.ToString(),
            student.AdmissionDate,
            student.Notes,
            mapped,
            student.CreatedAt);
    }

    private static Profile ParseProfile(UpsertStudentRequest request)
    {
        var name = request.FullName.Trim();
        var guardian = request.GuardianName.Trim();
        var address = request.Address.Trim();
        if (name.Length is < 1 or > 200)
        {
            throw new DomainValidationException("VALIDATION", "Enter a student name up to 200 characters.");
        }

        if (guardian.Length is < 1 or > 200)
        {
            throw new DomainValidationException("VALIDATION", "Enter the guardian name.");
        }

        if (!ContactRules.IsPhone(request.GuardianPhone))
        {
            throw new DomainValidationException("VALIDATION", "Enter a guardian phone with 9 to 15 digits.");
        }

        if (!string.IsNullOrWhiteSpace(request.GuardianPhoneSecondary) && !ContactRules.IsPhone(request.GuardianPhoneSecondary))
        {
            throw new DomainValidationException("VALIDATION", "The second phone number is not valid.");
        }

        if (address.Length is < 1 or > 300)
        {
            throw new DomainValidationException("VALIDATION", "Enter an address up to 300 characters.");
        }

        if ((request.City?.Trim().Length ?? 0) > 80)
        {
            throw new DomainValidationException("VALIDATION", "City must be 80 characters or fewer.");
        }

        if ((request.Notes?.Trim().Length ?? 0) > 2000)
        {
            throw new DomainValidationException("VALIDATION", "Notes must be 2000 characters or fewer.");
        }

        var genderText = string.IsNullOrWhiteSpace(request.Gender) ? nameof(StudentGender.Unspecified) : request.Gender;
        if (!Enum.TryParse<StudentGender>(genderText, true, out var gender))
        {
            throw new DomainValidationException("INVALID_GENDER", "Gender must be Female, Male, Other, or Unspecified.");
        }

        var statusText = string.IsNullOrWhiteSpace(request.Status) ? nameof(StudentStatus.Active) : request.Status;
        if (!Enum.TryParse<StudentStatus>(statusText, true, out var status))
        {
            throw new DomainValidationException("INVALID_STATUS", "Status must be Active, Inactive, or Left.");
        }

        return new Profile(
            name,
            guardian,
            request.GuardianPhone.Trim(),
            string.IsNullOrWhiteSpace(request.GuardianPhoneSecondary) ? null : request.GuardianPhoneSecondary.Trim(),
            address,
            string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim(),
            request.DateOfBirth,
            gender,
            status,
            request.AdmissionDate ?? ColomboClock.Today(),
            string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim());
    }

    private static void Apply(Student student, Profile profile)
    {
        student.FullName = profile.FullName;
        student.GuardianName = profile.GuardianName;
        student.GuardianPhone = profile.GuardianPhone;
        student.GuardianPhoneSecondary = profile.GuardianPhoneSecondary;
        student.Address = profile.Address;
        student.City = profile.City;
        student.DateOfBirth = profile.DateOfBirth;
        student.Gender = profile.Gender;
        student.Status = profile.Status;
        student.AdmissionDate = profile.AdmissionDate;
        student.Notes = profile.Notes;
    }

    private static string PhotoExtension(string fileName, string? contentType)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var allowed = contentType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/jpeg" => ".jpg",
            _ => extension is ".png" or ".webp" or ".jpg" or ".jpeg" ? extension : null
        };
        if (allowed is null && contentType is "application/octet-stream" && extension is ".png" or ".webp" or ".jpg" or ".jpeg")
        {
            allowed = extension == ".jpeg" ? ".jpg" : extension;
        }

        if (allowed is ".jpeg")
        {
            allowed = ".jpg";
        }

        return allowed ?? throw new DomainValidationException("PHOTO_TYPE", "Upload a JPEG, PNG, or WebP photo.");
    }

    private sealed record Profile(
        string FullName,
        string GuardianName,
        string GuardianPhone,
        string? GuardianPhoneSecondary,
        string Address,
        string? City,
        DateOnly? DateOfBirth,
        StudentGender Gender,
        StudentStatus Status,
        DateOnly AdmissionDate,
        string? Notes);
}
