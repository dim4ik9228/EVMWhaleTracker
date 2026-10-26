using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPolymarketPolygonSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ActivationBlock",
                table: "PolymarketSetting",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlockHash",
                table: "PolymarketEvent",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "BlockNumber",
                table: "PolymarketEvent",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "Canonical",
                table: "PolymarketEvent",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DecoderVersion",
                table: "PolymarketEvent",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RawLogId",
                table: "PolymarketEvent",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "CoveredBlock",
                table: "PolymarketCheckpoint",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoveredBlockHash",
                table: "PolymarketCheckpoint",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistryVersion",
                table: "PolymarketCheckpoint",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ScanNextBlock",
                table: "PolymarketCheckpoint",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ScanTargetBlock",
                table: "PolymarketCheckpoint",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PolymarketBlock",
                columns: table => new
                {
                    Number = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Hash = table.Column<string>(type: "text", nullable: false),
                    ParentHash = table.Column<string>(type: "text", nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolymarketBlock", x => x.Number);
                });

            migrationBuilder.CreateTable(
                name: "PolymarketRawLog",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ChainId = table.Column<int>(type: "integer", nullable: false),
                    Contract = table.Column<string>(type: "text", nullable: false),
                    BlockNumber = table.Column<long>(type: "bigint", nullable: false),
                    BlockHash = table.Column<string>(type: "text", nullable: false),
                    TransactionHash = table.Column<string>(type: "text", nullable: false),
                    TransactionIndex = table.Column<int>(type: "integer", nullable: false),
                    LogIndex = table.Column<int>(type: "integer", nullable: false),
                    TopicsJson = table.Column<string>(type: "text", nullable: false),
                    Data = table.Column<string>(type: "text", nullable: false),
                    Removed = table.Column<bool>(type: "boolean", nullable: false),
                    StoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecodeState = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolymarketRawLog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PolymarketRawLog_DecodeState_BlockNumber",
                table: "PolymarketRawLog",
                columns: new[] { "DecodeState", "BlockNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_PolymarketRawLog_StoredAt",
                table: "PolymarketRawLog",
                column: "StoredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PolymarketBlock");

            migrationBuilder.DropTable(
                name: "PolymarketRawLog");

            migrationBuilder.DropColumn(
                name: "ActivationBlock",
                table: "PolymarketSetting");

            migrationBuilder.DropColumn(
                name: "BlockHash",
                table: "PolymarketEvent");

            migrationBuilder.DropColumn(
                name: "BlockNumber",
                table: "PolymarketEvent");

            migrationBuilder.DropColumn(
                name: "Canonical",
                table: "PolymarketEvent");

            migrationBuilder.DropColumn(
                name: "DecoderVersion",
                table: "PolymarketEvent");

            migrationBuilder.DropColumn(
                name: "RawLogId",
                table: "PolymarketEvent");

            migrationBuilder.DropColumn(
                name: "CoveredBlock",
                table: "PolymarketCheckpoint");

            migrationBuilder.DropColumn(
                name: "CoveredBlockHash",
                table: "PolymarketCheckpoint");

            migrationBuilder.DropColumn(
                name: "RegistryVersion",
                table: "PolymarketCheckpoint");

            migrationBuilder.DropColumn(
                name: "ScanNextBlock",
                table: "PolymarketCheckpoint");

            migrationBuilder.DropColumn(
                name: "ScanTargetBlock",
                table: "PolymarketCheckpoint");
        }
    }
}
