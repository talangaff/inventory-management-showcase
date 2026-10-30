using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace inventory_management.Migrations
{
    /// <inheritdoc />
    public partial class AddShelfSlotStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Location",
                table: "shelf_units");

            migrationBuilder.AddColumn<int>(
                name: "status",
                table: "shelf_units",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "storage_unit_id",
                table: "shelf_units",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "label_printed_at_utc",
                table: "inventory_listings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "storage_unit_id",
                table: "boxes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "inventory_item_storage_unit_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    item_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    item_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    storage_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_item_storage_unit_assignments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shelf_slot_statuses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shelf_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shelf_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shelf_slot_statuses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "storage_units",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    identifier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    barcode_number = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storage_units", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_storage_unit_assignments_username_item_id",
                table: "inventory_item_storage_unit_assignments",
                columns: new[] { "username", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shelf_slot_statuses_shelf_unit_id_shelf_number",
                table: "shelf_slot_statuses",
                columns: new[] { "shelf_unit_id", "shelf_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storage_units_barcode_number",
                table: "storage_units",
                column: "barcode_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storage_units_identifier",
                table: "storage_units",
                column: "identifier",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_item_storage_unit_assignments");

            migrationBuilder.DropTable(
                name: "shelf_slot_statuses");

            migrationBuilder.DropTable(
                name: "storage_units");

            migrationBuilder.DropColumn(
                name: "status",
                table: "shelf_units");

            migrationBuilder.DropColumn(
                name: "storage_unit_id",
                table: "shelf_units");

            migrationBuilder.DropColumn(
                name: "label_printed_at_utc",
                table: "inventory_listings");

            migrationBuilder.DropColumn(
                name: "storage_unit_id",
                table: "boxes");

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "shelf_units",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");
        }
    }
}
