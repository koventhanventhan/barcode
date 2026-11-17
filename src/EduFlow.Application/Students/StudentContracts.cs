namespace EduFlow.Application.Students;

public sealed record StudentEnrollmentDto(Guid Id, Guid BatchId, string BatchName, string BatchCode, bool IsActive);

public sealed record StudentListItemDto(
    Guid Id,
    string StudentCode,
    string FullName,
    string GuardianName,
    string GuardianPhone,
    Guid EducationLevelId,
    string EducationLevelName,
    string Status,
    string BatchNames,
    string? PhotoUrl,
    Guid? BranchId,
    string? BranchName);

public sealed record StudentDto(
    Guid Id,
    string StudentCode,
    string FullName,
    DateOnly? DateOfBirth,
    string Gender,
    string? PhotoUrl,
    string GuardianName,
    string GuardianPhone,
    string? GuardianPhoneSecondary,
    string Address,
    string? City,
    Guid EducationLevelId,
    string EducationLevelName,
    Guid? BranchId,
    string Status,
    DateOnly AdmissionDate,
    string? Notes,
    IReadOnlyList<StudentEnrollmentDto> Enrollments,
    DateTimeOffset CreatedAt);

public sealed record StudentPageDto(
    IReadOnlyList<StudentListItemDto> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record StudentListQuery(
    string? Search,
    string? Status,
    Guid? EducationLevelId,
    Guid? BatchId,
    int Page = 1,
    int PageSize = 50);

public sealed record UpsertStudentRequest(
    string FullName,
    string GuardianName,
    string GuardianPhone,
    string Address,
    Guid EducationLevelId,
    string? GuardianPhoneSecondary = null,
    string? City = null,
    DateOnly? DateOfBirth = null,
    string? Gender = null,
    string? Status = null,
    DateOnly? AdmissionDate = null,
    string? Notes = null,
    Guid? BranchId = null,
    IReadOnlyList<Guid>? BatchIds = null);

public sealed record StudentImportReport(
    int Imported,
    int Failed,
    IReadOnlyList<EduFlow.Application.Academics.StudentImportError> Errors);

public sealed record BulkStudentRequest(IReadOnlyList<Guid>? StudentIds, bool All = false);
