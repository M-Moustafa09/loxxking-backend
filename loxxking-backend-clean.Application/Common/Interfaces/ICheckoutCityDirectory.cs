namespace loxxking_backend_clean.Application.Common.Interfaces;

/// <summary>
/// The CRM's city list for one of the store's countries: the names its couriers are matched on.
/// Read from the CRM and cached; the checkout suggestions and the order's city both use it.
/// </summary>
public interface ICheckoutCityDirectory
{
    /// <returns>The cities, or null when the CRM could not be read (the caller carries on without them).</returns>
    Task<IReadOnlyList<string>?> GetCitiesAsync(Guid countryId, CancellationToken cancellationToken);
}
