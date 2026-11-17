using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7Exams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExamReminderHoursBefore",
                table: "TenantNotificationConfigs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "GradingSchemes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradingSchemes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Exams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EducationLevelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GradingSchemeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeriesKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Room = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ReminderDaysBefore = table.Column<int>(type: "int", nullable: true),
                    ReminderHoursBefore = table.Column<int>(type: "int", nullable: true),
                    IncludeMarksInNotification = table.Column<bool>(type: "bit", nullable: false),
                    ScheduledNotifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReminderSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Exams_AcademicBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "AcademicBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Exams_EducationLevels_EducationLevelId",
                        column: x => x.EducationLevelId,
                        principalTable: "EducationLevels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Exams_GradingSchemes_GradingSchemeId",
                        column: x => x.GradingSchemeId,
                        principalTable: "GradingSchemes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GradeBoundaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GradingSchemeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    MinPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    MaxPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    GradePoint = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeBoundaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradeBoundaries_GradingSchemes_GradingSchemeId",
                        column: x => x.GradingSchemeId,
                        principalTable: "GradingSchemes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TotalMarks = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    MaxMarks = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    Percentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    BatchRank = table.Column<int>(type: "int", nullable: true),
                    LevelRank = table.Column<int>(type: "int", nullable: true),
                    AttendancePercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    TeacherRemarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PrincipalRemarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MarksReleasedNotified = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamResults_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamResults_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamSubjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaxMarks = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Room = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamSubjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamSubjects_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamSubjects_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamMarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamSubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Marks = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: true),
                    IsAbsent = table.Column<bool>(type: "bit", nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    SubjectRank = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamMarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamMarks_ExamSubjects_ExamSubjectId",
                        column: x => x.ExamSubjectId,
                        principalTable: "ExamSubjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamMarks_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamMarks_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamMarks_ExamId",
                table: "ExamMarks",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamMarks_ExamSubjectId",
                table: "ExamMarks",
                column: "ExamSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamMarks_StudentId",
                table: "ExamMarks",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamMarks_TenantId",
                table: "ExamMarks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamMarks_TenantId_ExamSubjectId_StudentId",
                table: "ExamMarks",
                columns: new[] { "TenantId", "ExamSubjectId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamResults_ExamId",
                table: "ExamResults",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamResults_StudentId",
                table: "ExamResults",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamResults_TenantId",
                table: "ExamResults",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamResults_TenantId_ExamId_StudentId",
                table: "ExamResults",
                columns: new[] { "TenantId", "ExamId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Exams_BatchId",
                table: "Exams",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_EducationLevelId",
                table: "Exams",
                column: "EducationLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_GradingSchemeId",
                table: "Exams",
                column: "GradingSchemeId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_TenantId",
                table: "Exams",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_TenantId_BatchId_Status",
                table: "Exams",
                columns: new[] { "TenantId", "BatchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Exams_TenantId_SeriesKey_EducationLevelId",
                table: "Exams",
                columns: new[] { "TenantId", "SeriesKey", "EducationLevelId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamSubjects_ExamId",
                table: "ExamSubjects",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSubjects_SubjectId",
                table: "ExamSubjects",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSubjects_TenantId",
                table: "ExamSubjects",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSubjects_TenantId_ExamId_SubjectId",
                table: "ExamSubjects",
                columns: new[] { "TenantId", "ExamId", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_GradeBoundaries_GradingSchemeId",
                table: "GradeBoundaries",
                column: "GradingSchemeId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeBoundaries_TenantId",
                table: "GradeBoundaries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeBoundaries_TenantId_GradingSchemeId_SortOrder",
                table: "GradeBoundaries",
                columns: new[] { "TenantId", "GradingSchemeId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_GradingSchemes_TenantId",
                table: "GradingSchemes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_GradingSchemes_TenantId_Name",
                table: "GradingSchemes",
                columns: new[] { "TenantId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExamMarks");

            migrationBuilder.DropTable(
                name: "ExamResults");

            migrationBuilder.DropTable(
                name: "GradeBoundaries");

            migrationBuilder.DropTable(
                name: "ExamSubjects");

            migrationBuilder.DropTable(
                name: "Exams");

            migrationBuilder.DropTable(
                name: "GradingSchemes");

            migrationBuilder.DropColumn(
                name: "ExamReminderHoursBefore",
                table: "TenantNotificationConfigs");
        }
    }
}
