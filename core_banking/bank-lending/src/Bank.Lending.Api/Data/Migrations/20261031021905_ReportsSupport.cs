using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Lending.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReportsSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "closed_on",
                table: "loan",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "loan_classification_change",
                columns: table => new
                {
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    from_classification = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    to_classification = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    days_past_due = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_classification_change", x => new { x.loan_id, x.date });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loan_classification_change");

            migrationBuilder.DropColumn(
                name: "closed_on",
                table: "loan");
        }
    }
}
