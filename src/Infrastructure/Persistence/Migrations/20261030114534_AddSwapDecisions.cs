using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSwapDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SwapDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Chain = table.Column<int>(type: "integer", nullable: false),
                    TransactionHash = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    WalletAddress = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    BlockHash = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    BlockNumber = table.Column<long>(type: "bigint", nullable: false),
                    FirstObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WalletRevision = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EntitlementRevision = table.Column<long>(type: "bigint", nullable: true),
                    EntitlementValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ObservationsJson = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: true),
                    LeaseOwner = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SwapDecisions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SwapDecisions_Chain_TransactionHash_UserId_WalletId",
                table: "SwapDecisions",
                columns: new[] { "Chain", "TransactionHash", "UserId", "WalletId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SwapDecisions_ExpiresAt",
                table: "SwapDecisions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_SwapDecisions_State_LeaseUntil",
                table: "SwapDecisions",
                columns: new[] { "State", "LeaseUntil" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SwapDecisions");
        }
    }
}
