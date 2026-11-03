using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Agent.Host.Data.Migrations
{
    /// <inheritdoc />
    public partial class WidenMessageKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "chat_message",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "text",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldDefaultValue: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "chat_message",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "text",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldDefaultValue: "text");
        }
    }
}
