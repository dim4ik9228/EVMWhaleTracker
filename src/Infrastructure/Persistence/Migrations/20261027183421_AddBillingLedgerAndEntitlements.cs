using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingLedgerAndEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BillingPaused",
                table: "PolymarketSetting",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BillingPaused",
                table: "HyperliquidSetting",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AccessGrant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReversedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OperatorId = table.Column<long>(type: "bigint", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessGrant", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingAccount",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    FreeWalletIds = table.Column<string>(type: "text", nullable: false),
                    FreePolymarketWalletId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectionInitialized = table.Column<bool>(type: "boolean", nullable: false),
                    WasPro = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingAccount", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "BillingAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingAudit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingCheckpoint",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    Offset = table.Column<long>(type: "bigint", nullable: false),
                    RetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Attention = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCheckpoint", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "BillingOutbox",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingOutbox", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "PaymentReceipt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    EventKey = table.Column<string>(type: "text", nullable: false),
                    ChargeId = table.Column<string>(type: "text", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    Reversal = table.Column<bool>(type: "boolean", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Evidence = table.Column<string>(type: "text", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReceipt", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Plan = table.Column<string>(type: "text", nullable: false),
                    DurationHours = table.Column<int>(type: "integer", nullable: false),
                    UsdPrice = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    StarsPrice = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CheckoutExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Fulfilled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrder", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAttempt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: true),
                    InvoiceId = table.Column<string>(type: "text", nullable: true),
                    CheckoutUrl = table.Column<string>(type: "text", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: true),
                    PaidAmount = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Retries = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAttempt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentAttempt_PurchaseOrder_OrderId",
                        column: x => x.OrderId,
                        principalTable: "PurchaseOrder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessGrant_OrderId",
                table: "AccessGrant",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessGrant_UserId_EndsAt",
                table: "AccessGrant",
                columns: new[] { "UserId", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingAudit_UserId_At",
                table: "BillingAudit",
                columns: new[] { "UserId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingOutbox_SentAt_RetryAt",
                table: "BillingOutbox",
                columns: new[] { "SentAt", "RetryAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempt_OrderId_Provider",
                table: "PaymentAttempt",
                columns: new[] { "OrderId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempt_Provider_ExternalId",
                table: "PaymentAttempt",
                columns: new[] { "Provider", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReceipt_Provider_ChargeId",
                table: "PaymentReceipt",
                columns: new[] { "Provider", "ChargeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReceipt_Provider_EventKey",
                table: "PaymentReceipt",
                columns: new[] { "Provider", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrder_UserId_CreatedAt",
                table: "PurchaseOrder",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessGrant");

            migrationBuilder.DropTable(
                name: "BillingAccount");

            migrationBuilder.DropTable(
                name: "BillingAudit");

            migrationBuilder.DropTable(
                name: "BillingCheckpoint");

            migrationBuilder.DropTable(
                name: "BillingOutbox");

            migrationBuilder.DropTable(
                name: "PaymentAttempt");

            migrationBuilder.DropTable(
                name: "PaymentReceipt");

            migrationBuilder.DropTable(
                name: "PurchaseOrder");

            migrationBuilder.DropColumn(
                name: "BillingPaused",
                table: "PolymarketSetting");

            migrationBuilder.DropColumn(
                name: "BillingPaused",
                table: "HyperliquidSetting");
        }
    }
}
