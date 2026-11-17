using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase9BackupsAndEnterprise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformAnnouncements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PublishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformAnnouncements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformBackupRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Trigger = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StorageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DatabaseProvider = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SqlCommandSummary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RetentionDays = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformBackupRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsMaintenanceMode = table.Column<bool>(type: "bit", nullable: false),
                    MaintenanceMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PlatformBackupRetentionDays = table.Column<int>(type: "int", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaffAttendanceRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CheckInMinutes = table.Column<int>(type: "int", nullable: true),
                    CheckOutMinutes = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffAttendanceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffAttendanceRecords_Users_StaffUserId",
                        column: x => x.StaffUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffLeaveRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveType = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewNotes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffLeaveRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffLeaveRequests_Users_StaffUserId",
                        column: x => x.StaffUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenantBackupPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsScheduledEnabled = table.Column<bool>(type: "bit", nullable: false),
                    RetentionDays = table.Column<int>(type: "int", nullable: false),
                    MaxBackupsToKeep = table.Column<int>(type: "int", nullable: false),
                    PreferredHourUtc = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantBackupPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantBackupRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Trigger = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StorageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TotalRecords = table.Column<int>(type: "int", nullable: false),
                    UploadedFilesCount = table.Column<int>(type: "int", nullable: false),
                    SummaryJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantBackupRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantCleanupPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuditLogRetentionDays = table.Column<int>(type: "int", nullable: false),
                    NotificationRetentionDays = table.Column<int>(type: "int", nullable: false),
                    SoftDeletedRetentionDays = table.Column<int>(type: "int", nullable: false),
                    ArchiveBeforePurge = table.Column<bool>(type: "bit", nullable: false),
                    IsAutoCleanupEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LastRunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastPurgedAuditLogs = table.Column<int>(type: "int", nullable: false),
                    LastPurgedMessages = table.Column<int>(type: "int", nullable: false),
                    LastPurgedSoftDeleted = table.Column<int>(type: "int", nullable: false),
                    LastArchiveUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantCleanupPolicies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAnnouncements_IsActive_CreatedAt",
                table: "PlatformAnnouncements",
                columns: new[] { "IsActive", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformBackupRecords_CreatedAt",
                table: "PlatformBackupRecords",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformSettings_Key",
                table: "PlatformSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceRecords_StaffUserId",
                table: "StaffAttendanceRecords",
                column: "StaffUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceRecords_TenantId",
                table: "StaffAttendanceRecords",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffAttendanceRecords_TenantId_StaffUserId_Date",
                table: "StaffAttendanceRecords",
                columns: new[] { "TenantId", "StaffUserId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffLeaveRequests_StaffUserId",
                table: "StaffLeaveRequests",
                column: "StaffUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffLeaveRequests_TenantId",
                table: "StaffLeaveRequests",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffLeaveRequests_TenantId_StaffUserId_Status",
                table: "StaffLeaveRequests",
                columns: new[] { "TenantId", "StaffUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantBackupPolicies_TenantId",
                table: "TenantBackupPolicies",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantBackupRecords_TenantId",
                table: "TenantBackupRecords",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantBackupRecords_TenantId_CreatedAt",
                table: "TenantBackupRecords",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantCleanupPolicies_TenantId",
                table: "TenantCleanupPolicies",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformAnnouncements");

            migrationBuilder.DropTable(
                name: "PlatformBackupRecords");

            migrationBuilder.DropTable(
                name: "PlatformSettings");

            migrationBuilder.DropTable(
                name: "StaffAttendanceRecords");

            migrationBuilder.DropTable(
                name: "StaffLeaveRequests");

            migrationBuilder.DropTable(
                name: "TenantBackupPolicies");

            migrationBuilder.DropTable(
                name: "TenantBackupRecords");

            migrationBuilder.DropTable(
                name: "TenantCleanupPolicies");
        }
    }
}
