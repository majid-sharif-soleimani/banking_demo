using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Agent.Host.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "chat_message",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "kind",
                table: "chat_message");
        }
    }
}
