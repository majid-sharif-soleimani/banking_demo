using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Lending.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReportProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loan_classification_change");

            migrationBuilder.CreateTable(
                name: "arrears_movement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    classification = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    loans = table.Column<int>(type: "integer", nullable: false),
                    outstanding = table.Column<decimal>(type: "numeric", nullable: false),
                    reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arrears_movement", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "portfolio_movement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    product_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    loans = table.Column<int>(type: "integer", nullable: false),
                    outstanding = table.Column<decimal>(type: "numeric", nullable: false),
                    reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portfolio_movement", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_arrears_movement_business_date",
                table: "arrears_movement",
                column: "business_date");

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_movement_business_date",
                table: "portfolio_movement",
                column: "business_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "arrears_movement");

            migrationBuilder.DropTable(
                name: "portfolio_movement");

            migrationBuilder.CreateTable(
                name: "loan_classification_change",
                columns: table => new
                {
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    days_past_due = table.Column<int>(type: "integer", nullable: false),
                    from_classification = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    to_classification = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_classification_change", x => new { x.loan_id, x.date });
                });
        }
    }
}
