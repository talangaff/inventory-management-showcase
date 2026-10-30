using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace inventory_management.Migrations
{
    /// <inheritdoc />
    public partial class AddItemAssignmentSoldFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_sold",
                table: "inventory_item_storage_unit_assignments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_sold",
                table: "inventory_item_locations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_sold",
                table: "inventory_item_box_assignments",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_sold",
                table: "inventory_item_storage_unit_assignments");

            migrationBuilder.DropColumn(
                name: "is_sold",
                table: "inventory_item_locations");

            migrationBuilder.DropColumn(
                name: "is_sold",
                table: "inventory_item_box_assignments");
        }
    }
}
