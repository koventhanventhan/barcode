namespace EduFlow.Application.Academics;

public sealed record EducationLevelDto(
    Guid Id,
    string Code,
    string Name,
    string Description,
    int SortOrder,
    bool IsEnabled);

public sealed record UpdateLevelSelectionRequest(IReadOnlyList<Guid> EnabledLevelIds);

public sealed record BatchGroupDto(Guid Id, string Name, string Code, bool IsActive);

public sealed record UpsertBatchGroupRequest(string Name, string Code, bool IsActive = true);

public sealed record BatchDto(
    Guid Id,
    string Name,
    string Code,
    Guid EducationLevelId,
    string EducationLevelCode,
    string EducationLevelName,
    Guid? BranchId,
    string? BranchName,
    Guid? BatchGroupId,
    string? BatchGroupName,
    string? AcademicYear,
    bool IsActive,
    int StudentCount);

public sealed record UpsertBatchRequest(
    string Name,
    string Code,
    Guid EducationLevelId,
    Guid? BranchId,
    Guid? BatchGroupId,
    string? AcademicYear,
    bool IsActive = true);

public sealed record SubjectDto(
    Guid Id,
    string Name,
    string Code,
    Guid? EducationLevelId,
    string? EducationLevelCode,
    string? EducationLevelName,
    bool IsActive);

public sealed record UpsertSubjectRequest(
    string Name,
    string Code,
    Guid? EducationLevelId,
    bool IsActive = true);

public sealed record ClassScheduleDto(
    Guid Id,
    string Name,
    Guid BatchId,
    string BatchName,
    Guid SubjectId,
    string SubjectName,
    Guid TeacherUserId,
    string TeacherName,
    int Weekday,
    int StartMinutes,
    int EndMinutes,
    string? Room,
    bool IsActive);

public sealed record UpsertClassScheduleRequest(
    string? Name,
    Guid BatchId,
    Guid SubjectId,
    Guid TeacherUserId,
    int Weekday,
    int StartMinutes,
    int EndMinutes,
    string? Room,
    bool IsActive = true);

public sealed record TeacherOptionDto(Guid Id, string FullName, string RoleType);

public sealed record IdCardContent(
    string InstituteName,
    string PrimaryHex,
    byte[]? Logo,
    byte[]? Photo,
    string StudentName,
    string StudentCode,
    string LevelName,
    string BatchNames,
    bool[] BarcodeModules);

public sealed record DirectoryRow(
    string StudentCode,
    string FullName,
    string GuardianPhone,
    string LevelName,
    string Status);

public interface IBarcodeEncoder
{
    bool[] EncodeCode128(string content);
}

public interface IAcademicDocumentService
{
    byte[] BuildIdCards(IReadOnlyList<IdCardContent> cards, bool eightUp);
    byte[] BuildLabels(IReadOnlyList<IdCardContent> cards);
    byte[] BuildStudentDirectory(string instituteName, string primaryHex, IReadOnlyList<DirectoryRow> rows);
}

public interface IWorkbookFile
{
    IReadOnlyList<TabularRow> Read(Stream stream);
    byte[] Write(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows);
}

public sealed record StoredDownload(byte[] Content, string ContentType, string FileName);
