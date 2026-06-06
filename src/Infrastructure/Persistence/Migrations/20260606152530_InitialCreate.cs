using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrackedWallets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    MinimumTransactionValue = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    TrackIncomingTransfers = table.Column<bool>(type: "boolean", nullable: false),
                    TrackOutgoingTransfers = table.Column<bool>(type: "boolean", nullable: false),
                    TrackTokenTransfers = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedWallets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedWallets_UserId_Address",
                table: "TrackedWallets",
                columns: new[] { "UserId", "Address" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackedWallets");
        }
    }
}
