using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace inventory_management.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceHistoryChangeType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "change_type",
                table: "inventory_price_reduction_history",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "AutoReduction");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "change_type",
                table: "inventory_price_reduction_history");
        }
    }
}
