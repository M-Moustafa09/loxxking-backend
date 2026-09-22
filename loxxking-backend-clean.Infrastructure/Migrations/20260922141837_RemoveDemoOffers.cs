using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace loxxking_backend_clean.Infrastructure.Migrations
{
    /// <summary>
    /// Deletes the demo offers (owner decision 2026-09-22). Until the «إدارة العروض» screen there was
    /// no way to add an offer, so every row in these tables came from OfferSeeder: the 20% offer on
    /// the demo «Classic Oxford Cotton Shirt» and the «Complete Elegance Bundle». Loxxking.com was
    /// showing both (checked on the live API the same day). OfferSeeder is removed in the same change,
    /// so a later --seed run does not bring them back. Children first: the rows refer to their parents.
    /// </summary>
    public partial class RemoveDemoOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [OfferProducts];");
            migrationBuilder.Sql("DELETE FROM [Offers];");
            migrationBuilder.Sql("DELETE FROM [BundleOfferItems];");
            migrationBuilder.Sql("DELETE FROM [BundleOffers];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Demo data is not restored.
        }
    }
}
