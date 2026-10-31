using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Lending.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class StorePayoffQuote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "last_end_of_day",
                table: "loan",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "payoff_interest",
                table: "loan",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "payoff_penalty",
                table: "loan",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "payoff_principal",
                table: "loan",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "payoff_waived",
                table: "loan",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_end_of_day",
                table: "loan");

            migrationBuilder.DropColumn(
                name: "payoff_interest",
                table: "loan");

            migrationBuilder.DropColumn(
                name: "payoff_penalty",
                table: "loan");

            migrationBuilder.DropColumn(
                name: "payoff_principal",
                table: "loan");

            migrationBuilder.DropColumn(
                name: "payoff_waived",
                table: "loan");
        }
    }
}
