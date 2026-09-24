using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace loxxking_backend_clean.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderSyncRetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastSyncError",
                table: "Orders",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextSyncAttemptAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SyncAttempts",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_IsSynced_NextSyncAttemptAt",
                table: "Orders",
                columns: new[] { "IsSynced", "NextSyncAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_IsSynced_NextSyncAttemptAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "LastSyncError",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "NextSyncAttemptAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SyncAttempts",
                table: "Orders");
        }
    }
}
