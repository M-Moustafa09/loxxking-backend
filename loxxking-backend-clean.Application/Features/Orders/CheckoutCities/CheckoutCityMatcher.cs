using System.Text;

namespace loxxking_backend_clean.Application.Features.Orders.CheckoutCities;

/// <summary>
/// Finds the CRM city a customer meant when they typed it themselves (owner decision 2026-09-24).
///
/// The CRM matches the order's city text against each courier's cities, so «اسكندريه» for
/// «الإسكندرية» left the order without a courier until someone retyped it. The two are compared
/// after the spelling differences Arabic typists make every day are set aside (hamza forms of alef,
/// ة/ه, ى/ي, diacritics, tatweel, a leading «ال», spaces and punctuation), and the English name the
/// suggestions show counts too. Then one slip of a letter (two in a long name) is still accepted, but
/// only when no other city is as close: an unclear guess keeps the customer's text as typed.
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

        var exact = candidates.Where(c => c.Key == wanted).Select(c => c.City).Distinct().ToList();
        if (exact.Count > 0) return exact.Count == 1 ? exact[0] : null;

        var allowed = wanted.Length >= 9 ? 2 : wanted.Length >= 5 ? 1 : 0;
        if (allowed == 0) return null;

        var closest = candidates
            .Select(c => (c.City, Distance: Distance(wanted, c.Key)))
            .Where(c => c.Distance <= allowed)
            .GroupBy(c => c.Distance)
            .OrderBy(g => g.Key)
            .FirstOrDefault()?
            .Select(c => c.City)
            .Distinct()
            .ToList();

        return closest is { Count: 1 } ? closest[0] : null;
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

    /// <summary>Levenshtein distance: the letters added, removed or changed to get from one to the other.</summary>
    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
