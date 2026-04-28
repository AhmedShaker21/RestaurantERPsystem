using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantERP.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ShiftId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShiftId",
                table: "Expenses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ShiftId",
                table: "Orders",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ShiftId",
                table: "Expenses",
                column: "ShiftId");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Shifts_ShiftId",
                table: "Expenses",
                column: "ShiftId",
                principalTable: "Shifts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Shifts_ShiftId",
                table: "Orders",
                column: "ShiftId",
                principalTable: "Shifts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Shifts_ShiftId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Shifts_ShiftId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ShiftId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_ShiftId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "ShiftId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShiftId",
                table: "Expenses");
        }
    }
}
