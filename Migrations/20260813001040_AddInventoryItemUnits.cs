using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace inventory_management.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryItemUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: this project's migrations are a documentation trail only — deploy.sh and
            // Program.cs never call `dotnet ef database update`; the real schema changes are
            // applied by the ExecuteSqlRaw guards added to Program.cs alongside this migration.
            // The model snapshot had already drifted from several prior guard-only changes
            // (attention_ignored_*, is_transit_box, dead price-reduction columns — see
            // Program.cs history), which `dotnet ef migrations add` would otherwise have
            // bundled into this Up()/Down() as unrelated drops/renames. Trimmed out here so
            // this migration only reflects the actual change it's named for.
            migrationBuilder.DropIndex(
                name: "IX_inventory_item_storage_unit_assignments_username_item_id",
                table: "inventory_item_storage_unit_assignments");

            migrationBuilder.DropIndex(
                name: "IX_inventory_item_locations_username_item_id",
                table: "inventory_item_locations");

            migrationBuilder.DropIndex(
                name: "IX_inventory_item_box_assignments_username_item_id",
                table: "inventory_item_box_assignments");

            migrationBuilder.AddColumn<Guid>(
                name: "unit_id",
                table: "inventory_item_storage_unit_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "unit_id",
                table: "inventory_item_locations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "unit_id",
                table: "inventory_item_box_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "inventory_item_units",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_index = table.Column<int>(type: "integer", nullable: false),
                    barcode = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_sold = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    label_printed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_item_units", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_storage_unit_assignments_username_item_id",
                table: "inventory_item_storage_unit_assignments",
                columns: new[] { "username", "item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_storage_unit_assignments_username_unit_id",
                table: "inventory_item_storage_unit_assignments",
                columns: new[] { "username", "unit_id" },
                unique: true,
                filter: "unit_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_locations_username_item_id",
                table: "inventory_item_locations",
                columns: new[] { "username", "item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_locations_username_unit_id",
                table: "inventory_item_locations",
                columns: new[] { "username", "unit_id" },
                unique: true,
                filter: "unit_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_box_assignments_username_item_id",
                table: "inventory_item_box_assignments",
                columns: new[] { "username", "item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_box_assignments_username_unit_id",
                table: "inventory_item_box_assignments",
                columns: new[] { "username", "unit_id" },
                unique: true,
                filter: "unit_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_units_username_barcode",
                table: "inventory_item_units",
                columns: new[] { "username", "barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_units_username_listing_id",
                table: "inventory_item_units",
                columns: new[] { "username", "listing_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_item_units");

            migrationBuilder.DropIndex(
                name: "IX_inventory_item_storage_unit_assignments_username_item_id",
                table: "inventory_item_storage_unit_assignments");

            migrationBuilder.DropIndex(
                name: "IX_inventory_item_storage_unit_assignments_username_unit_id",
                table: "inventory_item_storage_unit_assignments");

            migrationBuilder.DropIndex(
                name: "IX_inventory_item_locations_username_item_id",
                table: "inventory_item_locations");

            migrationBuilder.DropIndex(
                name: "IX_inventory_item_locations_username_unit_id",
                table: "inventory_item_locations");

            migrationBuilder.DropIndex(
                name: "IX_inventory_item_box_assignments_username_item_id",
                table: "inventory_item_box_assignments");

            migrationBuilder.DropIndex(
                name: "IX_inventory_item_box_assignments_username_unit_id",
                table: "inventory_item_box_assignments");

            migrationBuilder.DropColumn(
                name: "unit_id",
                table: "inventory_item_storage_unit_assignments");

            migrationBuilder.DropColumn(
                name: "unit_id",
                table: "inventory_item_locations");

            migrationBuilder.DropColumn(
                name: "unit_id",
                table: "inventory_item_box_assignments");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_storage_unit_assignments_username_item_id",
                table: "inventory_item_storage_unit_assignments",
                columns: new[] { "username", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_locations_username_item_id",
                table: "inventory_item_locations",
                columns: new[] { "username", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_box_assignments_username_item_id",
                table: "inventory_item_box_assignments",
                columns: new[] { "username", "item_id" },
                unique: true);
        }
    }
}
