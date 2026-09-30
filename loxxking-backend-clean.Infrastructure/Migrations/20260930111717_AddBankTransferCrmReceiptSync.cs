using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace loxxking_backend_clean.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBankTransferCrmReceiptSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CrmReceiptAttempts",
                table: "BankTransfers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CrmReceiptLastError",
                table: "BankTransfers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CrmReceiptNextAttemptAt",
                table: "BankTransfers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CrmReceiptSentAt",
                table: "BankTransfers",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CrmReceiptAttempts",
                table: "BankTransfers");

            migrationBuilder.DropColumn(
                name: "CrmReceiptLastError",
                table: "BankTransfers");

            migrationBuilder.DropColumn(
                name: "CrmReceiptNextAttemptAt",
                table: "BankTransfers");

            migrationBuilder.DropColumn(
                name: "CrmReceiptSentAt",
                table: "BankTransfers");
        }
    }
}
