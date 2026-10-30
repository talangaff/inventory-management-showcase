using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace inventory_management.Migrations
{
    /// <inheritdoc />
    public partial class AddDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "BuyerZip",
                table: "recent_orders",
                newName: "buyer_zip");

            migrationBuilder.RenameColumn(
                name: "BuyerStreet2",
                table: "recent_orders",
                newName: "buyer_street2");

            migrationBuilder.RenameColumn(
                name: "BuyerStreet1",
                table: "recent_orders",
                newName: "buyer_street1");

            migrationBuilder.RenameColumn(
                name: "BuyerState",
                table: "recent_orders",
                newName: "buyer_state");

            migrationBuilder.RenameColumn(
                name: "BuyerPhone",
                table: "recent_orders",
                newName: "buyer_phone");

            migrationBuilder.RenameColumn(
                name: "BuyerCountry",
                table: "recent_orders",
                newName: "buyer_country");

            migrationBuilder.RenameColumn(
                name: "BuyerCity",
                table: "recent_orders",
                newName: "buyer_city");

            migrationBuilder.AlterColumn<string>(
                name: "buyer_zip",
                table: "recent_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "buyer_street2",
                table: "recent_orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "buyer_street1",
                table: "recent_orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "buyer_state",
                table: "recent_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "buyer_phone",
                table: "recent_orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "buyer_country",
                table: "recent_orders",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "buyer_city",
                table: "recent_orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "ebay_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    draft_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    cover_photo_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    condition_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    condition_description = table.Column<string>(type: "text", nullable: true),
                    price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    weight_pounds = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    length_in = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    width_in = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    height_in = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    category_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    synced_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ebay_drafts", x => x.id);
                });

            // shipping_boxes was managed by Program.cs SQL guards; create only if not exists
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS shipping_boxes (
                    id uuid PRIMARY KEY,
                    username character varying(100) NOT NULL,
                    name character varying(100) NOT NULL,
                    length_in numeric(8,2) NOT NULL,
                    width_in numeric(8,2) NOT NULL,
                    height_in numeric(8,2) NOT NULL,
                    weight_lbs integer NULL,
                    weight_oz numeric(6,1) NULL,
                    quantity integer NOT NULL DEFAULT 0,
                    updated_at_utc timestamp with time zone NOT NULL
                );");

            migrationBuilder.CreateTable(
                name: "user_draft_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description_template = table.Column<string>(type: "text", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_draft_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ebay_drafts_username_draft_id",
                table: "ebay_drafts",
                columns: new[] { "username", "draft_id" },
                unique: true);

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS IX_shipping_boxes_username_name
                ON shipping_boxes (username, name);");

            migrationBuilder.CreateIndex(
                name: "IX_user_draft_settings_username",
                table: "user_draft_settings",
                column: "username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ebay_drafts");

            migrationBuilder.DropTable(
                name: "user_draft_settings");

            migrationBuilder.RenameColumn(
                name: "buyer_zip",
                table: "recent_orders",
                newName: "BuyerZip");

            migrationBuilder.RenameColumn(
                name: "buyer_street2",
                table: "recent_orders",
                newName: "BuyerStreet2");

            migrationBuilder.RenameColumn(
                name: "buyer_street1",
                table: "recent_orders",
                newName: "BuyerStreet1");

            migrationBuilder.RenameColumn(
                name: "buyer_state",
                table: "recent_orders",
                newName: "BuyerState");

            migrationBuilder.RenameColumn(
                name: "buyer_phone",
                table: "recent_orders",
                newName: "BuyerPhone");

            migrationBuilder.RenameColumn(
                name: "buyer_country",
                table: "recent_orders",
                newName: "BuyerCountry");

            migrationBuilder.RenameColumn(
                name: "buyer_city",
                table: "recent_orders",
                newName: "BuyerCity");

            migrationBuilder.AlterColumn<string>(
                name: "BuyerZip",
                table: "recent_orders",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BuyerStreet2",
                table: "recent_orders",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BuyerStreet1",
                table: "recent_orders",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BuyerState",
                table: "recent_orders",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BuyerPhone",
                table: "recent_orders",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BuyerCountry",
                table: "recent_orders",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BuyerCity",
                table: "recent_orders",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}
