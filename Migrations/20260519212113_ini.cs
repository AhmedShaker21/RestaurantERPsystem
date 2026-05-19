using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantERP.Migrations
{
    /// <inheritdoc />
    public partial class ini : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Box/Unit pricing columns ──────────────────────────────
            migrationBuilder.AddColumn<bool>(
                name: "SellByBox",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "UnitsPerBox",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "BoxCostPrice",
                table: "Products",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BoxSellPrice",
                table: "Products",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "BoxBarcode",
                table: "Products",
                type: "nvarchar(max)",
                nullable: true);

            // ── Tax override column ───────────────────────────────────
            migrationBuilder.AddColumn<decimal>(
                name: "TaxRateOverride",
                table: "Products",
                type: "decimal(5,2)",
                nullable: true);

            // ── ProductBranches (many-to-many Product ↔ Branch) ──────
            migrationBuilder.CreateTable(
                name: "ProductBranches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    OverridePrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductBranches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductBranches_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductBranches_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductBranches_BranchId",
                table: "ProductBranches",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBranches_ProductId",
                table: "ProductBranches",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ProductBranches");
            migrationBuilder.DropColumn(name: "BoxBarcode", table: "Products");
            migrationBuilder.DropColumn(name: "BoxCostPrice", table: "Products");
            migrationBuilder.DropColumn(name: "BoxSellPrice", table: "Products");
            migrationBuilder.DropColumn(name: "SellByBox", table: "Products");
            migrationBuilder.DropColumn(name: "TaxRateOverride", table: "Products");
            migrationBuilder.DropColumn(name: "UnitsPerBox", table: "Products");
        }
    }
}