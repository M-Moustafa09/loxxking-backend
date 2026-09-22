using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace loxxking_backend_clean.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Orders",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            // Orders placed before this were all priced in one number, saved against their country;
            // label them with that country's currency (almost all are Egypt / EGP, since checkout
            // saved every guest order as Egypt).
            // EXEC = its own batch. The idempotent deploy script runs the whole migration as ONE batch, and
            // SQL Server compiles a batch before the columns added above exist ("Invalid column name").
            migrationBuilder.Sql("EXEC(N'" + (@"UPDATE o SET Currency = c.Currency FROM Orders o JOIN Countries c ON c.Id = o.CountryId WHERE o.Currency = N'';").Replace("'", "''") + "');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Orders");
        }
    }
}
