using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Bank.Messaging.IntegrationTests.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audited_test_event",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    performed_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audited_test_event", x => x.event_id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_message",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handler_name = table.Column<string>(type: "text", nullable: false),
                    schema_uri = table.Column<string>(type: "text", nullable: false),
                    performed_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_message", x => new { x.message_id, x.handler_name });
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
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_message", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "received_test_event",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_key = table.Column<string>(type: "text", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    performed_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_received_test_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "test_item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    performed_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_item", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_unsent",
                table: "outbox_message",
                column: "position",
                filter: "sent_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_received_test_event_business_key",
                table: "received_test_event",
                columns: new[] { "message_key", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audited_test_event");

            migrationBuilder.DropTable(
                name: "inbox_message");

            migrationBuilder.DropTable(
                name: "outbox_message");

            migrationBuilder.DropTable(
                name: "received_test_event");

            migrationBuilder.DropTable(
                name: "test_item");
        }
    }
}
