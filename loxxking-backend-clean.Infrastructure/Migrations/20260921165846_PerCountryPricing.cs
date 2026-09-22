using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace loxxking_backend_clean.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PerCountryPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductPrices_ProductId",
                table: "ProductPrices");

            migrationBuilder.AddColumn<decimal>(
                name: "InternationalOriginalPrice",
                table: "Products",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InternationalPrice",
                table: "Products",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalPrice",
                table: "ProductPrices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Countries",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "Countries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductPrices_ProductId_CountryId",
                table: "ProductPrices",
                columns: new[] { "ProductId", "CountryId" });

            // ── Data (owner decision 2026-09-21) ──────────────────────────────────────────────
            // 1. The store sells in the CRM's 16 countries, each in its own currency. Reuse the
            //    oldest existing row for a country (orders, visits and prices point at it) and give
            //    it its ISO code, Arabic name and the CRM's English name (the CRM maps orders by
            //    that name); insert the countries that are missing.
            // 2. Every other row — countries created automatically from visitors' IPs, and the
            //    duplicates that created — is switched off, not deleted (orders and visits keep it).
            // 3. The price every existing product had (in EGP) becomes its Egypt price.
            migrationBuilder.Sql(@"
DECLARE @Wanted TABLE (Code nvarchar(2), Name nvarchar(100), NameAr nvarchar(100), Currency nvarchar(10), Aliases nvarchar(400));
INSERT INTO @Wanted VALUES
 (N'IQ', N'Iraq',                 N'العراق',     N'IQD', N'|Iraq|'),
 (N'AE', N'United Arab Emirates', N'الإمارات',   N'AED', N'|United Arab Emirates|UAE|'),
 (N'QA', N'Qatar',                N'قطر',        N'QAR', N'|Qatar|'),
 (N'LY', N'Libya',                N'ليبيا',      N'LYD', N'|Libya|'),
 (N'OM', N'Oman',                 N'سلطنة عمان', N'OMR', N'|Oman|'),
 (N'PS', N'Palestine',            N'فلسطين',     N'ILS', N'|Palestine|State of Palestine|Palestinian Territory|'),
 (N'TR', N'Turkey',               N'تركيا',      N'TRY', N'|Turkey|Türkiye|Turkiye|'),
 (N'JO', N'Jordan',               N'الأردن',     N'JOD', N'|Jordan|'),
 (N'KW', N'Kuwait',               N'الكويت',     N'KWD', N'|Kuwait|'),
 (N'BH', N'Bahrain',              N'البحرين',    N'BHD', N'|Bahrain|'),
 (N'SA', N'Saudi Arabia',         N'السعودية',   N'SAR', N'|Saudi Arabia|'),
 (N'TN', N'Tunisia',              N'تونس',       N'TND', N'|Tunisia|'),
 (N'MA', N'Morocco',              N'المغرب',     N'MAD', N'|Morocco|'),
 (N'DZ', N'Algeria',              N'الجزائر',    N'DZD', N'|Algeria|'),
 (N'LB', N'Lebanon',              N'لبنان',      N'LBP', N'|Lebanon|'),
 (N'EG', N'Egypt',                N'مصر',        N'EGP', N'|Egypt|');

-- 1a. Claim the oldest existing row per wanted country.
UPDATE c SET Code = w.Code, Name = w.Name, NameAr = w.NameAr, Currency = w.Currency,
             IsActive = 1, IsDeleted = 0, UpdatedAt = SYSUTCDATETIME()
FROM Countries c
JOIN @Wanted w ON c.Id = (
    SELECT TOP 1 c2.Id FROM Countries c2
    WHERE w.Aliases LIKE N'%|' + c2.Name + N'|%'
    ORDER BY c2.CreatedAt, c2.Id);

-- 1b. Insert the ones the store has never seen.
INSERT INTO Countries (Id, Name, Currency, DefaultLanguage, IsDefault, CreatedAt, IsActive, IsDeleted, Code, NameAr)
SELECT NEWID(), w.Name, w.Currency, N'ar', 0, SYSUTCDATETIME(), 1, 0, w.Code, w.NameAr
FROM @Wanted w
WHERE NOT EXISTS (SELECT 1 FROM Countries c WHERE c.Code = w.Code);

-- 2. Everything else is not a country the store sells in.
UPDATE Countries SET IsActive = 0, UpdatedAt = SYSUTCDATETIME() WHERE Code IS NULL;

-- 3. Existing products: their one price (EGP) becomes the Egypt price.
DECLARE @Egypt uniqueidentifier = (SELECT Id FROM Countries WHERE Code = N'EG');
INSERT INTO ProductPrices (Id, ProductId, CountryId, Price, OriginalPrice, CreatedAt, IsActive, IsDeleted)
SELECT NEWID(), p.Id, @Egypt, p.BasePrice,
       CASE WHEN p.OriginalPrice > p.BasePrice THEN p.OriginalPrice END,
       SYSUTCDATETIME(), 1, 0
FROM Products p
WHERE @Egypt IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM ProductPrices pp WHERE pp.ProductId = p.Id AND pp.CountryId = @Egypt);

UPDATE pp SET OriginalPrice = p.OriginalPrice
FROM ProductPrices pp JOIN Products p ON p.Id = pp.ProductId
WHERE pp.CountryId = @Egypt AND pp.OriginalPrice IS NULL AND p.OriginalPrice > pp.Price;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductPrices_ProductId_CountryId",
                table: "ProductPrices");

            migrationBuilder.DropColumn(
                name: "InternationalOriginalPrice",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "InternationalPrice",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "OriginalPrice",
                table: "ProductPrices");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Countries");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "Countries");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPrices_ProductId",
                table: "ProductPrices",
                column: "ProductId");
        }
    }
}
