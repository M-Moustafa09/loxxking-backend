namespace loxxking_backend_clean.Domain.Entities.Countries;

public class Country : BaseEntity
{
    public string Name { get; private set; }
    public string Currency { get; private set; }
    public string DefaultLanguage { get; private set; }
    public bool IsDefault { get; private set; }

    /// <summary>
    /// ISO 3166-1 alpha-2 code ("LY"). The visitor's country is matched on this, not on the name:
    /// the geolocation service renamed Turkey to Türkiye, which created a second Turkey row.
    /// </summary>
    public string? Code { get; private set; }

    /// <summary>Arabic name, shown in the (Arabic-only) dashboard's country picker.</summary>
    public string? NameAr { get; private set; }

    // IsActive (BaseEntity) = the store sells there: the admin can price products for it and its
    // visitors see its currency. The list is fixed (the CRM's 16 countries, owner decision
    // 2026-09-21); visitors from any other country see the product's international USD price.

    private Country() { } // EF Core

    public static Country Create(string name, string currency, string defaultLanguage = "en", bool isDefault = false)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Domain_Country_NameRequired", nameof(name));
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Domain_Country_CurrencyRequired", nameof(currency));

        return new Country
        {
            Id = Guid.NewGuid(),
            Name = name,
            Currency = currency,
            DefaultLanguage = defaultLanguage,
            IsDefault = isDefault,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void UpdateDetails(string name, string currency, string defaultLanguage, bool isDefault)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Domain_Country_NameRequired", nameof(name));
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Domain_Country_CurrencyRequired", nameof(currency));

        Name = name;
        Currency = currency;
        DefaultLanguage = defaultLanguage;
        IsDefault = isDefault;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetAsDefault()
    {
        IsDefault = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RemoveDefault()
    {
        IsDefault = false;
        UpdatedAt = DateTime.UtcNow;
    }
}
