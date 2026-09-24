using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace loxxking_backend_clean.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixCashOnDeliveryPaymentMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The storefront sent 1 for cash on delivery, and 1 is DebitCard (PaymentMethod enum:
            // CreditCard 0, DebitCard 1, BankTransfer 2, CashOnDelivery 3). The store has never
            // processed a card payment, so every DebitCard order is a cash on delivery order.
            migrationBuilder.Sql("UPDATE Orders SET PaymentMethod = 3 WHERE PaymentMethod = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the corrected orders were cash on delivery all along.
        }
    }
}
