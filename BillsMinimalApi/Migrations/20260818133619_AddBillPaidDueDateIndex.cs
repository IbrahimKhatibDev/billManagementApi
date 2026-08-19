using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillsMinimalApi.Migrations
{
    /// <inheritdoc />
    public partial class AddBillPaidDueDateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Bills_Paid_DueDate",
                table: "Bills",
                columns: new[] { "Paid", "DueDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bills_Paid_DueDate",
                table: "Bills");
        }
    }
}
