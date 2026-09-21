using System.Text.RegularExpressions;

namespace loxxking_backend_clean.Application.Features.Categories.Commands;

/// <summary>What the dashboard's category screen sends that needs work before it reaches the table.</summary>
internal static class CategoryFields
{
    /// <summary>
    /// A readable slug from the English name — «Postpartum» → `postpartum`, a second one → `postpartum-2`.
    /// It used to carry eight random hex characters (`postpartum-3fa9c1b2`), so no link written by hand
    /// could ever point at a category. Deleted categories still hold their slug, so they count too.
    /// </summary>
    public static async Task<string> UniqueSlugAsync(IApplicationDbContext context, string nameEn, CancellationToken ct)
    {
        var baseSlug = Regex.Replace(nameEn.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (baseSlug.Length == 0) baseSlug = "category";

        var taken = await context.Categories
            .IgnoreQueryFilters()
            .Where(c => c.Slug == baseSlug || c.Slug.StartsWith(baseSlug + "-"))
            .Select(c => c.Slug)
            .ToListAsync(ct);

        var slug = baseSlug;
        for (var n = 2; taken.Contains(slug); n++) slug = $"{baseSlug}-{n}";
        return slug;
    }

    /// <summary>
    /// The screen sends a new image inline as a data URL, the same way the product screen does; it is
    /// stored and replaced by its URL. Anything else (an existing URL, or empty) is kept as it is.
    /// </summary>
    public static async Task<string> StoreImageAsync(IFileStorageService storage, string? image, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(image)) return string.Empty;
        if (!image.StartsWith("data:image/")) return image;

        var bytes = Convert.FromBase64String(image[(image.IndexOf(',') + 1)..]);
        using var stream = new MemoryStream(bytes);
        var ext = image.Split(';')[0].Split('/')[1];
        return await storage.UploadAsync(stream, $"{Guid.NewGuid()}.{ext}", $"image/{ext}", "categories", ct);
    }
}
