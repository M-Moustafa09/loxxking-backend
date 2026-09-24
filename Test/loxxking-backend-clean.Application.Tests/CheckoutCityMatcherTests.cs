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
    [InlineData("الاسكندرة", "الإسكندرية")]      // one letter missing
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
    [InlineData("Alexandrea", "الإسكندرية")]
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

    [Fact]
    public void Two_Equally_Close_Cities_Are_Not_Guessed_Between()
    {
        // «سمالود» is one letter from both: neither is picked, the text stays as typed.
        Assert.Null(CheckoutCityMatcher.Match("سمالود", new[] { "سمالوط", "سمالوك" }));
        Assert.Equal("سمالوط", CheckoutCityMatcher.Match("سمالود", new[] { "سمالوط", "سمنود" }));
    }

    [Fact]
    public void Short_Names_Need_An_Exact_Match()
    {
        var cities = new[] { "الحد", "الحلة" };
        Assert.Equal("الحد", CheckoutCityMatcher.Match("حد", cities));
        Assert.Null(CheckoutCityMatcher.Match("حل", cities));
    }
}
