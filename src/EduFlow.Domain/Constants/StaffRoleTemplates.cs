using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Constants;

public sealed record StaffRoleTemplate(
    string Key,
    string Name,
    RoleType RoleType,
    IReadOnlyList<string> Permissions);

public static class StaffRoleTemplates
{
    public static readonly IReadOnlyList<StaffRoleTemplate> All =
    [
        new(
            "front-desk",
            "Front desk",
            RoleType.Staff,
            [Permissions.StudentsView, Permissions.StudentsEdit, Permissions.AcademicsView, Permissions.FeesView]),
        new(
            "accountant",
            "Accountant",
            RoleType.Staff,
            [Permissions.FeesView, Permissions.FeesCollect, Permissions.ReportsView]),
        new(
            "class-teacher",
            "Class teacher",
            RoleType.Teacher,
            [Permissions.StudentsView, Permissions.AcademicsView, Permissions.AttendanceMark, Permissions.ExamsManage]),
        new(
            "registrar",
            "Registrar",
            RoleType.Staff,
            [
                Permissions.StudentsView,
                Permissions.StudentsEdit,
                Permissions.AcademicsView,
                Permissions.AcademicsManage,
                Permissions.UsersView,
                Permissions.BranchesView
            ])
    ];

    public static StaffRoleTemplate? Find(string? key) =>
        string.IsNullOrWhiteSpace(key)
            ? null
            : All.FirstOrDefault(t => string.Equals(t.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
}
