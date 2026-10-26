using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPolymarketProfileBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PolymarketCheckpoint",
                columns: table => new
                {
                    SourceAddress = table.Column<string>(type: "text", nullable: false),
                    LastSuccessfulScan = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LatestActivity = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WindowStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WindowEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Cursor = table.Column<string>(type: "text", nullable: true),
                    Gap = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolymarketCheckpoint", x => x.SourceAddress);
                });

            migrationBuilder.CreateTable(
                name: "PolymarketEvent",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    SourceAddress = table.Column<string>(type: "text", nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    StoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolymarketEvent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PolymarketSetting",
                columns: table => new
                {
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Generation = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceAddress = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: true),
                    ProposedAddress = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: true),
                    ProfileJson = table.Column<string>(type: "text", nullable: true),
                    NeedsAttention = table.Column<bool>(type: "boolean", nullable: false),
                    MinimumTradeValue = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolymarketSetting", x => x.WalletId);
                    table.ForeignKey(
                        name: "FK_PolymarketSetting_TrackedWallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "TrackedWallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PolymarketDelivery",
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
                    table.PrimaryKey("PK_PolymarketDelivery", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PolymarketDelivery_PolymarketEvent_EventId",
                        column: x => x.EventId,
                        principalTable: "PolymarketEvent",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PolymarketDelivery_TrackedWallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "TrackedWallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PolymarketDelivery_EventId_WalletId_Generation",
                table: "PolymarketDelivery",
                columns: new[] { "EventId", "WalletId", "Generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolymarketDelivery_State_DueAt",
                table: "PolymarketDelivery",
                columns: new[] { "State", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PolymarketDelivery_WalletId",
                table: "PolymarketDelivery",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_PolymarketEvent_SourceAddress_Time",
                table: "PolymarketEvent",
                columns: new[] { "SourceAddress", "Time" });

            migrationBuilder.CreateIndex(
                name: "IX_PolymarketEvent_StoredAt",
                table: "PolymarketEvent",
                column: "StoredAt");

            migrationBuilder.CreateIndex(
                name: "IX_PolymarketSetting_Enabled_SourceAddress",
                table: "PolymarketSetting",
                columns: new[] { "Enabled", "SourceAddress" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PolymarketCheckpoint");

            migrationBuilder.DropTable(
                name: "PolymarketDelivery");

            migrationBuilder.DropTable(
                name: "PolymarketSetting");

            migrationBuilder.DropTable(
                name: "PolymarketEvent");
        }
    }
}
