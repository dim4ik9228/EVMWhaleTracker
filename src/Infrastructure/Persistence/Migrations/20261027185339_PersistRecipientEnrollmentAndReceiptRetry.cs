using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistRecipientEnrollmentAndReceiptRetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingPauseReason",
                table: "PolymarketSetting",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Retries",
                table: "PaymentReceipt",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetryAt",
                table: "PaymentReceipt",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "BillingPauseReason",
                table: "HyperliquidSetting",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WalletAccessState",
                columns: table => new
                {
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountRevision = table.Column<long>(type: "bigint", nullable: false),
                    EnrollmentRevision = table.Column<long>(type: "bigint", nullable: false),
                    EvmEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ActiveSince = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EvmUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PolymarketUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    HyperliquidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletAccessState", x => x.WalletId);
                    table.ForeignKey(
                        name: "FK_WalletAccessState_TrackedWallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "TrackedWallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WalletAccessState");

            migrationBuilder.DropColumn(
                name: "BillingPauseReason",
                table: "PolymarketSetting");

            migrationBuilder.DropColumn(
                name: "Retries",
                table: "PaymentReceipt");

            migrationBuilder.DropColumn(
                name: "RetryAt",
                table: "PaymentReceipt");

            migrationBuilder.DropColumn(
                name: "BillingPauseReason",
                table: "HyperliquidSetting");
        }
    }
}
