using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace inventory_management.Migrations
{
    /// <inheritdoc />
    public partial class InactiveListings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "box_identifier",
                table: "boxes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "inventory_listings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    item_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    cover_photo_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_seen_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    inactivated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_listings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_boxes_username_box_identifier",
                table: "boxes",
                columns: new[] { "username", "box_identifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_listings_username_is_active",
                table: "inventory_listings",
                columns: new[] { "username", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_listings_username_item_id",
                table: "inventory_listings",
                columns: new[] { "username", "item_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_listings");

            migrationBuilder.DropIndex(
                name: "IX_boxes_username_box_identifier",
                table: "boxes");

            migrationBuilder.DropColumn(
                name: "box_identifier",
                table: "boxes");
        }
    }
}
