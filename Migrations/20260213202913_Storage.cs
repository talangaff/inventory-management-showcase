using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace inventory_management.Migrations
{
    /// <inheritdoc />
    public partial class Storage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Username",
                table: "user_ebay_connections",
                newName: "username");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "user_ebay_connections",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "user_ebay_connections",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "RefreshToken",
                table: "user_ebay_connections",
                newName: "refresh_token");

            migrationBuilder.RenameColumn(
                name: "ExpiresAtUtc",
                table: "user_ebay_connections",
                newName: "expires_at_utc");

            migrationBuilder.RenameColumn(
                name: "AccessToken",
                table: "user_ebay_connections",
                newName: "access_token");

            migrationBuilder.RenameIndex(
                name: "IX_user_ebay_connections_Username",
                table: "user_ebay_connections",
                newName: "IX_user_ebay_connections_username");

            migrationBuilder.CreateTable(
                name: "boxes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    box_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    shelf_unit_identifier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    shelf_number = table.Column<int>(type: "integer", nullable: false),
                    shelf_barcode = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_boxes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_item_box_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    item_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    item_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    box_id = table.Column<Guid>(type: "uuid", nullable: false),
                    box_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_item_box_assignments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_item_locations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    item_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    item_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    shelf_unit_identifier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    shelf_number = table.Column<int>(type: "integer", nullable: false),
                    shelf_barcode = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_item_locations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_boxes_username_box_number",
                table: "boxes",
                columns: new[] { "username", "box_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_box_assignments_username_item_id",
                table: "inventory_item_box_assignments",
                columns: new[] { "username", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_locations_username_item_id",
                table: "inventory_item_locations",
                columns: new[] { "username", "item_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "boxes");

            migrationBuilder.DropTable(
                name: "inventory_item_box_assignments");

            migrationBuilder.DropTable(
                name: "inventory_item_locations");

            migrationBuilder.RenameColumn(
                name: "username",
                table: "user_ebay_connections",
                newName: "Username");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "user_ebay_connections",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "user_ebay_connections",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "refresh_token",
                table: "user_ebay_connections",
                newName: "RefreshToken");

            migrationBuilder.RenameColumn(
                name: "expires_at_utc",
                table: "user_ebay_connections",
                newName: "ExpiresAtUtc");

            migrationBuilder.RenameColumn(
                name: "access_token",
                table: "user_ebay_connections",
                newName: "AccessToken");

            migrationBuilder.RenameIndex(
                name: "IX_user_ebay_connections_username",
                table: "user_ebay_connections",
                newName: "IX_user_ebay_connections_Username");
        }
    }
}
