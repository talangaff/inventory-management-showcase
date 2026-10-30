using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace inventory_management.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoRelistFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_relist_enabled",
                table: "inventory_listings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_price_reduction_utc",
                table: "inventory_listings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "listing_end_time_utc",
                table: "inventory_listings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "listing_type",
                table: "inventory_listings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "minimum_price",
                table: "inventory_listings",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "price_reduction_amount",
                table: "inventory_listings",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "price_reduction_months",
                table: "inventory_listings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "price_reduction_type",
                table: "inventory_listings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "relist_in_progress",
                table: "inventory_listings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "inventory_relist_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_item_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    new_item_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    relisted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_relist_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_inventory_relist_history_inventory_listings_listing_id",
                        column: x => x.listing_id,
                        principalTable: "inventory_listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_relist_history_listing_id",
                table: "inventory_relist_history",
                column: "listing_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_relist_history_username_listing_id",
                table: "inventory_relist_history",
                columns: new[] { "username", "listing_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_relist_history");

            migrationBuilder.DropColumn(
                name: "auto_relist_enabled",
                table: "inventory_listings");

            migrationBuilder.DropColumn(
                name: "last_price_reduction_utc",
                table: "inventory_listings");

            migrationBuilder.DropColumn(
                name: "listing_end_time_utc",
                table: "inventory_listings");

            migrationBuilder.DropColumn(
                name: "listing_type",
                table: "inventory_listings");

            migrationBuilder.DropColumn(
                name: "minimum_price",
                table: "inventory_listings");

            migrationBuilder.DropColumn(
                name: "price_reduction_amount",
                table: "inventory_listings");

            migrationBuilder.DropColumn(
                name: "price_reduction_months",
                table: "inventory_listings");

            migrationBuilder.DropColumn(
                name: "price_reduction_type",
                table: "inventory_listings");

            migrationBuilder.DropColumn(
                name: "relist_in_progress",
                table: "inventory_listings");
        }
    }
}
