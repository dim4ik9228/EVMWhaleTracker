using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHyperliquidDurableTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HyperliquidCheckpoint",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    Time = table.Column<long>(type: "bigint", nullable: false),
                    Gap = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HyperliquidCheckpoint", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "HyperliquidEvent",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    Stream = table.Column<string>(type: "text", nullable: false),
                    Time = table.Column<long>(type: "bigint", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    StoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HyperliquidEvent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HyperliquidReservation",
                columns: table => new
                {
                    Address = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HyperliquidReservation", x => x.Address);
                });

            migrationBuilder.CreateTable(
                name: "HyperliquidSetting",
                columns: table => new
                {
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Generation = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HyperliquidSetting", x => x.WalletId);
                    table.ForeignKey(
                        name: "FK_HyperliquidSetting_TrackedWallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "TrackedWallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HyperliquidDelivery",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<string>(type: "text", nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    Generation = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HyperliquidDelivery", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HyperliquidDelivery_HyperliquidEvent_EventId",
                        column: x => x.EventId,
                        principalTable: "HyperliquidEvent",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HyperliquidDelivery_TrackedWallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "TrackedWallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HyperliquidDelivery_EventId_WalletId_Generation",
                table: "HyperliquidDelivery",
                columns: new[] { "EventId", "WalletId", "Generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HyperliquidDelivery_State_DueAt",
                table: "HyperliquidDelivery",
                columns: new[] { "State", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HyperliquidDelivery_WalletId",
                table: "HyperliquidDelivery",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_HyperliquidEvent_Address_Stream_Time",
                table: "HyperliquidEvent",
                columns: new[] { "Address", "Stream", "Time" });

            migrationBuilder.CreateIndex(
                name: "IX_HyperliquidEvent_StoredAt",
                table: "HyperliquidEvent",
                column: "StoredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HyperliquidCheckpoint");

            migrationBuilder.DropTable(
                name: "HyperliquidDelivery");

            migrationBuilder.DropTable(
                name: "HyperliquidReservation");

            migrationBuilder.DropTable(
                name: "HyperliquidSetting");

            migrationBuilder.DropTable(
                name: "HyperliquidEvent");
        }
    }
}
