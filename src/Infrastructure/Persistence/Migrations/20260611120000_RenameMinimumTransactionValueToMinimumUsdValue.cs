using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameMinimumTransactionValueToMinimumUsdValue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MinimumTransactionValue",
                table: "TrackedWallets",
                newName: "MinimumUsdValue");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MinimumUsdValue",
                table: "TrackedWallets",
                newName: "MinimumTransactionValue");
        }
    }
}
