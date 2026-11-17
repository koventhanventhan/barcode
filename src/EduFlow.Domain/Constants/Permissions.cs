namespace EduFlow.Domain.Constants;

public static class Permissions
{
    public const string StudentsView = "students.view";
    public const string StudentsEdit = "students.edit";
    public const string AttendanceMark = "attendance.mark";
    public const string FeesView = "fees.view";
    public const string FeesCollect = "fees.collect";
    public const string ExamsManage = "exams.manage";
    public const string ReportsView = "reports.view";
    public const string SettingsManage = "settings.manage";
    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";
    public const string RolesView = "roles.view";
    public const string RolesManage = "roles.manage";
    public const string BranchesView = "branches.view";
    public const string BranchesManage = "branches.manage";

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(StudentsView, "View Students", "Students", "View student directory and profiles"),
        new(StudentsEdit, "Edit Students", "Students", "Create, update, and import students"),
        new(AttendanceMark, "Mark Attendance", "Attendance", "Scan barcodes and manage attendance registers"),
        new(FeesView, "View Fees", "Fees", "View fee structures, invoices, and analytics"),
        new(FeesCollect, "Collect Fees", "Fees", "Record payments and issue receipts"),
        new(ExamsManage, "Manage Exams", "Exams", "Create exams, enter marks, and publish results"),
        new(ReportsView, "View Reports", "Reports", "Access performance and operational reports"),
        new(SettingsManage, "Manage Settings", "Settings", "Configure institute settings, branding, and feature switches"),
        new(UsersView, "View Users", "Users", "View staff and teacher accounts"),
        new(UsersManage, "Manage Users", "Users", "Invite, update, and deactivate staff and teacher accounts"),
        new(RolesView, "View Roles", "Roles", "View custom roles and permission assignments"),
        new(RolesManage, "Manage Roles", "Roles", "Create and modify custom roles"),
        new(BranchesView, "View Branches", "Branches", "View institute branches"),
        new(BranchesManage, "Manage Branches", "Branches", "Create and update institute branches")
    ];

    public static readonly IReadOnlyList<string> DefaultStaffPermissions =
    [
        StudentsView,
        StudentsEdit,
        AttendanceMark,
        FeesView,
        FeesCollect,
        ReportsView,
        BranchesView
    ];

    public static readonly IReadOnlyList<string> DefaultTeacherPermissions =
    [
        StudentsView,
        AttendanceMark,
        ExamsManage,
        ReportsView,
        BranchesView
    ];
}

public sealed record PermissionDefinition(
    string Key,
    string Name,
    string Group,
    string Description);
