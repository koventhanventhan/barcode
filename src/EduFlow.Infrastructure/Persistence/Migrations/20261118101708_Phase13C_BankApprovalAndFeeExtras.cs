using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase13C_BankApprovalAndFeeExtras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoIssueOnAdmission",
                table: "FeeStructures",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastPrintedAt",
                table: "FeePayments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReceiptPrintCount",
                table: "FeePayments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditAppliedAmount",
                table: "FeeInvoices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "BankPaymentSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedByRole = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    TransferDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SlipUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SlipFileName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    SlipContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    SlipBytes = table.Column<long>(type: "bigint", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    FeePaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankPaymentSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankPaymentSubmissions_FeeInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "FeeInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankPaymentSubmissions_FeePayments_FeePaymentId",
                        column: x => x.FeePaymentId,
                        principalTable: "FeePayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankPaymentSubmissions_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SiblingDiscountRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    DiscountType = table.Column<int>(type: "int", nullable: false),
                    SecondChildValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ThirdPlusChildValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ApplyToMonthly = table.Column<bool>(type: "bit", nullable: false),
                    ApplyToOneTime = table.Column<bool>(type: "bit", nullable: false),
                    ApplyToAdmission = table.Column<bool>(type: "bit", nullable: false),
                    StackWithConcession = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiblingDiscountRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudentCreditTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryType = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SignedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Method = table.Column<int>(type: "int", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FeePaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentCreditTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentCreditTransactions_FeeInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "FeeInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentCreditTransactions_FeePayments_FeePaymentId",
                        column: x => x.FeePaymentId,
                        principalTable: "FeePayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentCreditTransactions_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenantBankAccountSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    BranchName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    AccountName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    AccountNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Instructions = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantBankAccountSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankPaymentSubmissions_BranchId",
                table: "BankPaymentSubmissions",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_BankPaymentSubmissions_FeePaymentId",
                table: "BankPaymentSubmissions",
                column: "FeePaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_BankPaymentSubmissions_InvoiceId",
                table: "BankPaymentSubmissions",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_BankPaymentSubmissions_StudentId",
                table: "BankPaymentSubmissions",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_BankPaymentSubmissions_TenantId",
                table: "BankPaymentSubmissions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BankPaymentSubmissions_TenantId_InvoiceId",
                table: "BankPaymentSubmissions",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankPaymentSubmissions_TenantId_Status_CreatedAt",
                table: "BankPaymentSubmissions",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BankPaymentSubmissions_TenantId_StudentId",
                table: "BankPaymentSubmissions",
                columns: new[] { "TenantId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_SiblingDiscountRules_TenantId",
                table: "SiblingDiscountRules",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentCreditTransactions_BranchId",
                table: "StudentCreditTransactions",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentCreditTransactions_FeePaymentId",
                table: "StudentCreditTransactions",
                column: "FeePaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentCreditTransactions_InvoiceId",
                table: "StudentCreditTransactions",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentCreditTransactions_StudentId",
                table: "StudentCreditTransactions",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentCreditTransactions_TenantId",
                table: "StudentCreditTransactions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentCreditTransactions_TenantId_OccurredAt_EntryType",
                table: "StudentCreditTransactions",
                columns: new[] { "TenantId", "OccurredAt", "EntryType" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentCreditTransactions_TenantId_StudentId_OccurredAt",
                table: "StudentCreditTransactions",
                columns: new[] { "TenantId", "StudentId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantBankAccountSettings_TenantId",
                table: "TenantBankAccountSettings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankPaymentSubmissions");

            migrationBuilder.DropTable(
                name: "SiblingDiscountRules");

            migrationBuilder.DropTable(
                name: "StudentCreditTransactions");

            migrationBuilder.DropTable(
                name: "TenantBankAccountSettings");

            migrationBuilder.DropColumn(
                name: "AutoIssueOnAdmission",
                table: "FeeStructures");

            migrationBuilder.DropColumn(
                name: "LastPrintedAt",
                table: "FeePayments");

            migrationBuilder.DropColumn(
                name: "ReceiptPrintCount",
                table: "FeePayments");

            migrationBuilder.DropColumn(
                name: "CreditAppliedAmount",
                table: "FeeInvoices");
        }
    }
}
