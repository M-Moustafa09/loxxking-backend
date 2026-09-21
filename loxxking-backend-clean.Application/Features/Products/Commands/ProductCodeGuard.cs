namespace loxxking_backend_clean.Application.Features.Products.Commands;

/// <summary>
/// One CRM product ↔ one store product: the code decides the warehouse of every synced order line, so
/// two live products sharing a code would be indistinguishable to the CRM. Deleted products are hidden
/// by the query filter, so deleting a product frees its code.
/// </summary>
internal static class ProductCodeGuard
{
    public static async Task<bool> IsTakenAsync(IApplicationDbContext context, string? productCode, Guid? exceptProductId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(productCode)) return false;
        var code = productCode.Trim();
        return await context.Products.AnyAsync(p => p.ProductCode == code && p.Id != exceptProductId, ct);
    }
}
