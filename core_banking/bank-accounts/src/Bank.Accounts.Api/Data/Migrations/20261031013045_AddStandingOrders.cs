using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStandingOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "end_of_day_run",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    standing_orders_run = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_end_of_day_run", x => x.date);
                });

            migrationBuilder.CreateTable(
                name: "standing_order",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    next_run_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_standing_order", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "standing_order_run",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    standing_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_standing_order_run", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_standing_order_status_next_run_date",
                table: "standing_order",
                columns: new[] { "status", "next_run_date" });

            migrationBuilder.CreateIndex(
                name: "ix_standing_order_run_standing_order_id_run_date",
                table: "standing_order_run",
                columns: new[] { "standing_order_id", "run_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "end_of_day_run");

            migrationBuilder.DropTable(
                name: "standing_order");

            migrationBuilder.DropTable(
                name: "standing_order_run");
        }
    }
}
