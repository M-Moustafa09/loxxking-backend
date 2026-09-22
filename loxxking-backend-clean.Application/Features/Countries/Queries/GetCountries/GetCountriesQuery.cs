namespace loxxking_backend_clean.Application.Features.Countries.Queries.GetCountries;

public record GetCountriesQuery() : IRequest<Result<List<GetCountriesResponse>>>;

/// <param name="Code">ISO 3166-1 alpha-2 ("LY").</param>
/// <param name="NameAr">Arabic name for the dashboard's country picker.</param>
public record GetCountriesResponse(Guid Id, string Name, string Currency, string DefaultLanguage, string? Code = null, string? NameAr = null);
