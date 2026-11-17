using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase13A_MultiBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PriceMonthly",
                table: "Plans",
                newName: "PriceMonthlyLkr");

            migrationBuilder.AddColumn<decimal>(
                name: "ExtraSmsPerSegmentLkr",
                table: "Plans",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExtraStoragePerGbLkr",
                table: "Plans",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SetupFeeLkr",
                table: "Plans",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PriceYearlyLkr",
                table: "Plans",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "FeePayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "FeeInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "ClassSessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "ClassSchedules",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "AttendanceMarks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StaffBranchAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffBranchAssignments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeePayments_BranchId",
                table: "FeePayments",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_FeeInvoices_BranchId",
                table: "FeeInvoices",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_BranchId",
                table: "ClassSessions",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSchedules_BranchId",
                table: "ClassSchedules",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceMarks_BranchId",
                table: "AttendanceMarks",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffBranchAssignments_TenantId",
                table: "StaffBranchAssignments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffBranchAssignments_TenantId_UserId_BranchId",
                table: "StaffBranchAssignments",
                columns: new[] { "TenantId", "UserId", "BranchId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffBranchAssignments");

            migrationBuilder.DropIndex(
                name: "IX_FeePayments_BranchId",
                table: "FeePayments");

            migrationBuilder.DropIndex(
                name: "IX_FeeInvoices_BranchId",
                table: "FeeInvoices");

            migrationBuilder.DropIndex(
                name: "IX_ClassSessions_BranchId",
                table: "ClassSessions");

            migrationBuilder.DropIndex(
                name: "IX_ClassSchedules_BranchId",
                table: "ClassSchedules");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceMarks_BranchId",
                table: "AttendanceMarks");

            migrationBuilder.DropColumn(
                name: "ExtraSmsPerSegmentLkr",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "ExtraStoragePerGbLkr",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "SetupFeeLkr",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "PriceYearlyLkr",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "FeePayments");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "FeeInvoices");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "ClassSchedules");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "AttendanceMarks");

            migrationBuilder.RenameColumn(
                name: "PriceMonthlyLkr",
                table: "Plans",
                newName: "PriceMonthly");
        }
    }
}
