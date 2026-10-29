using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSettledChargeUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentReceipt_Provider_ChargeId",
                table: "PaymentReceipt");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReceipt_Provider_ChargeId",
                table: "PaymentReceipt",
                columns: new[] { "Provider", "ChargeId" },
                unique: true,
                filter: "\"State\" = 'Applied'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentReceipt_Provider_ChargeId",
                table: "PaymentReceipt");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReceipt_Provider_ChargeId",
                table: "PaymentReceipt",
                columns: new[] { "Provider", "ChargeId" });
        }
    }
}
