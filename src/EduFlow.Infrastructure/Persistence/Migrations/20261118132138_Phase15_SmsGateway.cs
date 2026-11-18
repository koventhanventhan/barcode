using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase15_SmsGateway : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowCustomSmsSenderId",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SmsSenderId",
                table: "Tenants",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GatewayCost",
                table: "NotificationOutboxes",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GatewaySegments",
                table: "NotificationOutboxes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlatformSmsSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    EncryptedApiKey = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    SenderId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PromotionalSenderId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BaseUrl = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TestMode = table.Column<bool>(type: "bit", nullable: true),
                    TestAllowList = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LowBalanceThreshold = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LastLowBalanceAlertSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastKnownBalance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    LastGatewayActive = table.Column<bool>(type: "bit", nullable: true),
                    BalanceCheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSmsSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmsGatewayErrorLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsGatewayErrorLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformSmsSettings_Key",
                table: "PlatformSmsSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmsGatewayErrorLogs_OccurredAt",
                table: "SmsGatewayErrorLogs",
                column: "OccurredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformSmsSettings");

            migrationBuilder.DropTable(
                name: "SmsGatewayErrorLogs");

            migrationBuilder.DropColumn(
                name: "AllowCustomSmsSenderId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SmsSenderId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "GatewayCost",
                table: "NotificationOutboxes");

            migrationBuilder.DropColumn(
                name: "GatewaySegments",
                table: "NotificationOutboxes");
        }
    }
}
