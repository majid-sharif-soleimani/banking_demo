using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Messaging.IntegrationTests.Migrations
{
    /// <inheritdoc />
    public partial class AddVersionToMessagingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "outbox_message",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "inbox_message",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "outbox_message");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "inbox_message");
        }
    }
}
