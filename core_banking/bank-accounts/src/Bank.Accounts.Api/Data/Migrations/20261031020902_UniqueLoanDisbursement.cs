using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueLoanDisbursement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_account_entry_loan_disbursement",
                table: "account_entry",
                column: "reference",
                unique: true,
                filter: "kind = 'LoanDisbursement'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_account_entry_loan_disbursement",
                table: "account_entry");
        }
    }
}
