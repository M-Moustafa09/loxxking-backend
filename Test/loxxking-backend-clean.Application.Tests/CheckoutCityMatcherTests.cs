using loxxking_backend_clean.Application.Features.Orders.CheckoutCities;

namespace loxxking_backend_clean.Application.Tests;

public class CheckoutCityMatcherTests
{
    private static readonly string[] Egypt =
        { "القاهرة", "الإسكندرية", "الجيزة", "شرم الشيخ", "بني سويف", "المنيا", "المنوفية", "الغردقة" };

    [Theory]
    [InlineData("الإسكندرية", "الإسكندرية")]
    [InlineData("الاسكندرية", "الإسكندرية")]
    [InlineData("اسكندريه", "الإسكندرية")]
    [InlineData("  إسكندرية  ", "الإسكندرية")]
    [InlineData("الإسْكَنْدَرِيَّة", "الإسكندرية")]
    [InlineData("القاهره", "القاهرة")]
    [InlineData("قاهرة", "القاهرة")]
    [InlineData("شرم الشيخ", "شرم الشيخ")]
    [InlineData("شرم  الشيخ", "شرم الشيخ")]
    [InlineData("شرم-الشيخ", "شرم الشيخ")]
    [InlineData("بنى سويف", "بني سويف")]
    [InlineData("الغردقه", "الغردقة")]
    public void Typed_Arabic_Spellings_Find_The_Crm_City(string typed, string expected)
        => Assert.Equal(expected, CheckoutCityMatcher.Match(typed, Egypt));

    [Theory]
    [InlineData("Alexandria", "الإسكندرية")]
    [InlineData("alexandria", "الإسكندرية")]
    [InlineData("Cairo", "القاهرة")]
    public void The_English_Name_Finds_The_Crm_City(string typed, string expected)
        => Assert.Equal(expected, CheckoutCityMatcher.Match(typed, Egypt));

    [Theory]
    [InlineData("المنصورة")]   // not one of the CRM's cities
    [InlineData("طنطا")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_City_The_Crm_Does_Not_Have_Is_Not_Guessed(string? typed)
        => Assert.Null(CheckoutCityMatcher.Match(typed, Egypt));

    [Theory]
    [InlineData("الاسكندرة")]      // one letter missing
    [InlineData("الاسكندريا")]     // one letter different
    [InlineData("Alexandrea")]
    public void A_Different_Letter_Is_Never_Guessed(string typed)
        => Assert.Null(CheckoutCityMatcher.Match(typed, Egypt));

    [Fact]
    public void Two_Places_A_Letter_Apart_Stay_Apart()
    {
        // Two real Libyan places the CRM deliberately keeps apart (CamexCityMatcher).
        var libya = new[] { "ورشفانه", "الزاوية" };
        Assert.Null(CheckoutCityMatcher.Match("ورشفاته", libya));
        Assert.Equal("ورشفانه", CheckoutCityMatcher.Match("ورشفانه", libya));
        Assert.Equal("الزاوية", CheckoutCityMatcher.Match("الزاويه", libya));
    }

    [Fact]
    public void Short_Names_Match_Only_Their_Own_Spelling()
    {
        var cities = new[] { "الحد", "الحلة" };
        Assert.Equal("الحد", CheckoutCityMatcher.Match("حد", cities));
        Assert.Null(CheckoutCityMatcher.Match("حل", cities));
    }
}
