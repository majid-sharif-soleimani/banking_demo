using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEntryBusinessDateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_account_entry_business_date_sequence",
                table: "account_entry",
                columns: new[] { "business_date", "sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_account_entry_business_date_sequence",
                table: "account_entry");
        }
    }
}
