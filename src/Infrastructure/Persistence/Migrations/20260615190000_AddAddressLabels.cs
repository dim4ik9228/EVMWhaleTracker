using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAddressLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AddressLabels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    Chain = table.Column<int>(type: "integer", nullable: true),
                    Label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddressLabels", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AddressLabels_Address_Chain",
                table: "AddressLabels",
                columns: new[] { "Address", "Chain" },
                unique: true,
                filter: "\"IsEnabled\" = TRUE AND \"Chain\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AddressLabels_Address_Global",
                table: "AddressLabels",
                column: "Address",
                unique: true,
                filter: "\"IsEnabled\" = TRUE AND \"Chain\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AddressLabels_Chain",
                table: "AddressLabels",
                column: "Chain");

            migrationBuilder.CreateIndex(
                name: "IX_AddressLabels_Kind",
                table: "AddressLabels",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_AddressLabels_Source",
                table: "AddressLabels",
                column: "Source");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AddressLabels");
        }
    }
}
