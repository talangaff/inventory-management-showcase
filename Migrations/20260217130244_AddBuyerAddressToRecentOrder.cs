using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace inventory_management.Migrations
{
    /// <inheritdoc />
    public partial class AddBuyerAddressToRecentOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inventory_listings_barcode",
                table: "inventory_listings");

            migrationBuilder.AddColumn<string>(
                name: "currency_id",
                table: "inventory_listings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "current_price",
                table: "inventory_listings",
                type: "numeric",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recent_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    item_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    item_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    item_photo_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    buyer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    has_tracking = table.Column<bool>(type: "boolean", nullable: false),
                    is_retrieved = table.Column<bool>(type: "boolean", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    BuyerStreet1 = table.Column<string>(type: "text", nullable: true),
                    BuyerStreet2 = table.Column<string>(type: "text", nullable: true),
                    BuyerCity = table.Column<string>(type: "text", nullable: true),
                    BuyerState = table.Column<string>(type: "text", nullable: true),
                    BuyerZip = table.Column<string>(type: "text", nullable: true),
                    BuyerCountry = table.Column<string>(type: "text", nullable: true),
                    BuyerPhone = table.Column<string>(type: "text", nullable: true),
                    order_created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_seen_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recent_orders", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_recent_orders_username_order_created_at_utc",
                table: "recent_orders",
                columns: new[] { "username", "order_created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_recent_orders_username_order_id_item_id",
                table: "recent_orders",
                columns: new[] { "username", "order_id", "item_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recent_orders");

            migrationBuilder.DropColumn(
                name: "currency_id",
                table: "inventory_listings");

            migrationBuilder.DropColumn(
                name: "current_price",
                table: "inventory_listings");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_listings_barcode",
                table: "inventory_listings",
                column: "barcode",
                unique: true);
        }
    }
}
