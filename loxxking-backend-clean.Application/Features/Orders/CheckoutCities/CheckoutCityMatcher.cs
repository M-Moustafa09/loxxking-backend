using System.Text;

namespace loxxking_backend_clean.Application.Features.Orders.CheckoutCities;

/// <summary>
/// Finds the CRM city a customer meant when they typed it themselves (owner decision 2026-09-24).
///
/// The CRM matches the order's city text against each courier's cities, so «اسكندريه» for
/// «الإسكندرية» left the order without a courier until someone retyped it. The two are compared
/// after the spelling differences Arabic typists make every day are set aside (hamza forms of alef,
/// ة/ه, ى/ي, diacritics, tatweel, a leading «ال», spaces and punctuation), and the English name the
/// suggestions show counts too.
///
/// A different letter is never accepted, even one: «ورشفانه» and «ورشفاته» are two places, and a
/// wrong guess ships the parcel to the wrong city without anyone noticing. The CRM matches cities
/// for its couriers the same way (CamexCityMatcher, 2026-09-24 review), so a name the customer typed
/// that is not clearly one of the CRM's cities is kept as typed for staff to check.
/// </summary>
public static class CheckoutCityMatcher
{
    /// <returns>The CRM's own spelling of the city, or null when none is clearly meant.</returns>
    public static string? Match(string? typed, IEnumerable<string> cities)
    {
        var wanted = Normalize(typed);
        if (wanted.Length == 0) return null;

        // Each CRM city under its Arabic name and, when it has one, its English name.
        var candidates = cities
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .SelectMany(c => new[] { (City: c, Key: Normalize(c)), (City: c, Key: Normalize(CheckoutCityEnglishNames.For(c))) })
            .Where(c => c.Key.Length > 0)
            .ToList();

        var matches = candidates.Where(c => c.Key == wanted).Select(c => c.City).Distinct().ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>The spelling-free form two names are compared in.</summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var text = new StringBuilder(name.Length);
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            // Arabic diacritics (harakat, shadda, sukun, superscript alef) and tatweel.
            if (ch is >= 'ً' and <= 'ٟ' or 'ٰ' or 'ـ') continue;
            text.Append(ch switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ة' => 'ه',
                'ى' or 'ئ' => 'ي',
                'ؤ' => 'و',
                _ => char.IsLetterOrDigit(ch) ? ch : ' '
            });
        }

        // «ال» (or «al»/«el») at the start of a word is left out: «الاسكندريه» = «اسكندريه».
        var words = text.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 3 && w.StartsWith("ال", StringComparison.Ordinal) ? w[2..] : w)
            .Where(w => w is not ("al" or "el"));

        return string.Concat(words).Normalize(NormalizationForm.FormC);
    }
}
