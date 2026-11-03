using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Lending.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationQueueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_loan_application_queue",
                table: "loan_application",
                columns: new[] { "status", "created_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_loan_application_queue",
                table: "loan_application");
        }
    }
}
