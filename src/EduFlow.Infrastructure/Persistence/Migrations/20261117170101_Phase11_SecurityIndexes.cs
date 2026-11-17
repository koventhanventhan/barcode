using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase11_SecurityIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Students_TenantId_FullName",
                table: "Students",
                columns: new[] { "TenantId", "FullName" });

            migrationBuilder.CreateIndex(
                name: "IX_Students_TenantId_Status_EducationLevelId",
                table: "Students",
                columns: new[] { "TenantId", "Status", "EducationLevelId" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentEnrollments_TenantId_BatchId_IsActive",
                table: "StudentEnrollments",
                columns: new[] { "TenantId", "BatchId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutboxes_TenantId_CreatedAt_MessageType_Status",
                table: "NotificationOutboxes",
                columns: new[] { "TenantId", "CreatedAt", "MessageType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FeePayments_TenantId_StudentId_PaidAt",
                table: "FeePayments",
                columns: new[] { "TenantId", "StudentId", "PaidAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_TenantId_SessionDate_Status",
                table: "ClassSessions",
                columns: new[] { "TenantId", "SessionDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassSchedules_TenantId_BatchId_IsActive",
                table: "ClassSchedules",
                columns: new[] { "TenantId", "BatchId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassSchedules_TenantId_Weekday_StartMinutes",
                table: "ClassSchedules",
                columns: new[] { "TenantId", "Weekday", "StartMinutes" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceMarks_TenantId_StudentId_IsUndone_MarkedAt",
                table: "AttendanceMarks",
                columns: new[] { "TenantId", "StudentId", "IsUndone", "MarkedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Students_TenantId_FullName",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_Students_TenantId_Status_EducationLevelId",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_StudentEnrollments_TenantId_BatchId_IsActive",
                table: "StudentEnrollments");

            migrationBuilder.DropIndex(
                name: "IX_NotificationOutboxes_TenantId_CreatedAt_MessageType_Status",
                table: "NotificationOutboxes");

            migrationBuilder.DropIndex(
                name: "IX_FeePayments_TenantId_StudentId_PaidAt",
                table: "FeePayments");

            migrationBuilder.DropIndex(
                name: "IX_ClassSessions_TenantId_SessionDate_Status",
                table: "ClassSessions");

            migrationBuilder.DropIndex(
                name: "IX_ClassSchedules_TenantId_BatchId_IsActive",
                table: "ClassSchedules");

            migrationBuilder.DropIndex(
                name: "IX_ClassSchedules_TenantId_Weekday_StartMinutes",
                table: "ClassSchedules");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceMarks_TenantId_StudentId_IsUndone_MarkedAt",
                table: "AttendanceMarks");
        }
    }
}
