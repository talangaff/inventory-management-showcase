using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace inventory_management.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceReductionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_price_reduction_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    new_price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    reduced_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_price_reduction_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_inventory_price_reduction_history_inventory_listings_listin~",
                        column: x => x.listing_id,
                        principalTable: "inventory_listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_price_reduction_history_listing_id",
                table: "inventory_price_reduction_history",
                column: "listing_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_price_reduction_history_username_listing_id",
                table: "inventory_price_reduction_history",
                columns: new[] { "username", "listing_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_price_reduction_history");
        }
    }
}
