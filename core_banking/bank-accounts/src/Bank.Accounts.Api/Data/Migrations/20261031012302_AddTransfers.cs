using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transfer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    converted_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    target_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    fx_rate = table.Column<decimal>(type: "numeric", nullable: true),
                    fx_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    fx_observed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    fx_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    standing_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transfer_from_account_id",
                table: "transfer",
                column: "from_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_to_account_id",
                table: "transfer",
                column: "to_account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transfer");
        }
    }
}
