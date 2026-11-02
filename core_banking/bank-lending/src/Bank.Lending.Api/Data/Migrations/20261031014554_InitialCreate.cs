using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Bank.Lending.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_ref",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_ref", x => x.account_id);
                });

            migrationBuilder.CreateTable(
                name: "business_day",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_day", x => x.date);
                });

            migrationBuilder.CreateTable(
                name: "currency_ref",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    minor_units = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currency_ref", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "customer_ref",
                columns: table => new
                {
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_ref", x => x.customer_id);
                });

            migrationBuilder.CreateTable(
                name: "end_of_day_run",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_end_of_day_run", x => x.date);
                });

            migrationBuilder.CreateTable(
                name: "inbox_message",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handler_name = table.Column<string>(type: "text", nullable: false),
                    schema_uri = table.Column<string>(type: "text", nullable: false),
                    performed_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_message", x => new { x.message_id, x.handler_name });
                });

            migrationBuilder.CreateTable(
                name: "loan",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repayment_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    principal = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    minor_units = table.Column<int>(type: "integer", nullable: false),
                    repayment_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    annual_rate = table.Column<decimal>(type: "numeric", nullable: false),
                    term_months = table.Column<int>(type: "integer", nullable: false),
                    penalty_daily_rate = table.Column<decimal>(type: "numeric", nullable: false),
                    grace_days = table.Column<int>(type: "integer", nullable: false),
                    overdue_after_days = table.Column<int>(type: "integer", nullable: false),
                    default_after_days = table.Column<int>(type: "integer", nullable: false),
                    payoff_min_percent = table.Column<decimal>(type: "numeric", nullable: false),
                    payoff_cap = table.Column<decimal>(type: "numeric", nullable: false),
                    max_stages = table.Column<int>(type: "integer", nullable: true),
                    disbursed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    classification = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    pending_payoff_collection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "loan_application",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requested_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    term_months = table.Column<int>(type: "integer", nullable: false),
                    repayment_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposed_stages = table.Column<string>(type: "jsonb", nullable: true),
                    purpose = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    loan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_application", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "loan_day_run",
                columns: table => new
                {
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_day_run", x => new { x.loan_id, x.date });
                });

            migrationBuilder.CreateTable(
                name: "loan_payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    principal = table.Column<decimal>(type: "numeric", nullable: false),
                    interest = table.Column<decimal>(type: "numeric", nullable: false),
                    penalty = table.Column<decimal>(type: "numeric", nullable: false),
                    unallocated = table.Column<decimal>(type: "numeric", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_payment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "loan_product",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    repayment_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    min_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    max_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    min_term_months = table.Column<int>(type: "integer", nullable: false),
                    max_term_months = table.Column<int>(type: "integer", nullable: false),
                    annual_rate = table.Column<decimal>(type: "numeric", nullable: false),
                    penalty_daily_rate = table.Column<decimal>(type: "numeric", nullable: false),
                    grace_days = table.Column<int>(type: "integer", nullable: false),
                    overdue_after_days = table.Column<int>(type: "integer", nullable: false),
                    default_after_days = table.Column<int>(type: "integer", nullable: false),
                    payoff_min_percent = table.Column<decimal>(type: "numeric", nullable: false),
                    payoff_cap = table.Column<decimal>(type: "numeric", nullable: false),
                    max_stages = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_product", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    schema_uri = table.Column<string>(type: "text", nullable: false),
                    message_key = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    performed_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_message", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "schedule_item",
                columns: table => new
                {
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    principal_due = table.Column<decimal>(type: "numeric", nullable: false),
                    interest_due = table.Column<decimal>(type: "numeric", nullable: false),
                    penalty_due = table.Column<decimal>(type: "numeric", nullable: false),
                    paid_principal = table.Column<decimal>(type: "numeric", nullable: false),
                    paid_interest = table.Column<decimal>(type: "numeric", nullable: false),
                    paid_penalty = table.Column<decimal>(type: "numeric", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    pending_collection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_item", x => new { x.loan_id, x.number });
                    table.ForeignKey(
                        name: "fk_schedule_item_loan_loan_id",
                        column: x => x.loan_id,
                        principalTable: "loan",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "loan_application_status_change",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    to_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_application_status_change", x => x.id);
                    table.ForeignKey(
                        name: "fk_loan_application_status_change_loan_application_application",
                        column: x => x.application_id,
                        principalTable: "loan_application",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "loan_product",
                columns: new[] { "code", "annual_rate", "created_at", "currency", "default_after_days", "grace_days", "max_amount", "max_stages", "max_term_months", "min_amount", "min_term_months", "name", "overdue_after_days", "payoff_cap", "payoff_min_percent", "penalty_daily_rate", "performed_by", "repayment_type" },
                values: new object[,]
                {
                    { "CAR", 0.0695m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SEK", 90, 5, 600000m, null, 84, 20000m, 12, "Car loan", 1, 5000m, 0.01m, 0.0005m, "system", "Annuity" },
                    { "MORTGAGE", 0.0425m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SEK", 90, 5, 10000000m, null, 360, 500000m, 60, "Mortgage", 1, 20000m, 0.01m, 0.0003m, "system", "Annuity" },
                    { "PURCHASE", 0.085m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SEK", 90, 5, 300000m, 4, 24, 10000m, 3, "Purchase financing", 1, 3000m, 0.01m, 0.0005m, "system", "Staged" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_loan_application_id",
                table: "loan",
                column: "application_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loan_customer_id",
                table: "loan",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_loan_application_customer_id",
                table: "loan_application",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_loan_application_status_change_application_id",
                table: "loan_application_status_change",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_loan_payment_loan_id",
                table: "loan_payment",
                column: "loan_id");

            migrationBuilder.CreateIndex(
                name: "ix_loan_payment_source_id",
                table: "loan_payment",
                column: "source_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_unsent",
                table: "outbox_message",
                column: "position",
                filter: "sent_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_item_pending_collection_id",
                table: "schedule_item",
                column: "pending_collection_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_ref");

            migrationBuilder.DropTable(
                name: "business_day");

            migrationBuilder.DropTable(
                name: "currency_ref");

            migrationBuilder.DropTable(
                name: "customer_ref");

            migrationBuilder.DropTable(
                name: "end_of_day_run");

            migrationBuilder.DropTable(
                name: "inbox_message");

            migrationBuilder.DropTable(
                name: "loan_application_status_change");

            migrationBuilder.DropTable(
                name: "loan_day_run");

            migrationBuilder.DropTable(
                name: "loan_payment");

            migrationBuilder.DropTable(
                name: "loan_product");

            migrationBuilder.DropTable(
                name: "outbox_message");

            migrationBuilder.DropTable(
                name: "schedule_item");

            migrationBuilder.DropTable(
                name: "loan_application");

            migrationBuilder.DropTable(
                name: "loan");
        }
    }
}
