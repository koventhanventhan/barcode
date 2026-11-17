using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5Attendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendancePolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LateAfterMinutes = table.Column<int>(type: "int", nullable: false),
                    MarkAbsentOnStop = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendancePolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClassSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClassScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StoppedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StartedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JoinCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    StartedNotified = table.Column<bool>(type: "bit", nullable: false),
                    StoppedNotified = table.Column<bool>(type: "bit", nullable: false),
                    CancelledNotified = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassSessions_ClassSchedules_ClassScheduleId",
                        column: x => x.ClassScheduleId,
                        principalTable: "ClassSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceMarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClassSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    MarkedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    MarkedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsUndone = table.Column<bool>(type: "bit", nullable: false),
                    UndoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UndoReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PresentNotified = table.Column<bool>(type: "bit", nullable: false),
                    AbsentNotified = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceMarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceMarks_ClassSessions_ClassSessionId",
                        column: x => x.ClassSessionId,
                        principalTable: "ClassSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceMarks_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceMarks_ClassSessionId",
                table: "AttendanceMarks",
                column: "ClassSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceMarks_StudentId",
                table: "AttendanceMarks",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceMarks_TenantId",
                table: "AttendanceMarks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceMarks_TenantId_ClassSessionId_StudentId",
                table: "AttendanceMarks",
                columns: new[] { "TenantId", "ClassSessionId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePolicies_TenantId",
                table: "AttendancePolicies",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_ClassScheduleId",
                table: "ClassSessions",
                column: "ClassScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_TenantId",
                table: "ClassSessions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_TenantId_ClassScheduleId_SessionDate",
                table: "ClassSessions",
                columns: new[] { "TenantId", "ClassScheduleId", "SessionDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceMarks");

            migrationBuilder.DropTable(
                name: "AttendancePolicies");

            migrationBuilder.DropTable(
                name: "ClassSessions");
        }
    }
}
