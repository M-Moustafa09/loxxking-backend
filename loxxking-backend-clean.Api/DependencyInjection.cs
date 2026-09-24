using loxxking_backend_clean.Api.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace loxxking_backend_clean.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddApi(this IServiceCollection services)
    {
        services.AddControllers();
        services.AddMemoryCache();
        services.AddDistributedMemoryCache();
        services.AddHttpContextAccessor();
        services.AddScoped<loxxking_backend_clean.Application.Common.Interfaces.ICurrentUserService, loxxking_backend_clean.Api.Services.CurrentUserService>();
        services.AddScoped<loxxking_backend_clean.Application.Common.Interfaces.ISupportNotificationService, loxxking_backend_clean.Api.Services.SupportNotificationService>();
        services.AddScoped<loxxking_backend_clean.Application.Common.Interfaces.ICatalogChangeNotifier, loxxking_backend_clean.Api.Services.CatalogChangeNotifier>();
        services.AddScoped<loxxking_backend_clean.Application.Common.Interfaces.ICheckoutCityDirectory, loxxking_backend_clean.Api.Services.CheckoutCityDirectory>();

        services.AddExceptionHandler<ValidationExceptionHandler>();
        services.AddProblemDetails();

        services.AddLocalization();
        services.AddSingleton<loxxking_backend_clean.Shared.Resources.SharedResourceLocalizer>();
        services.AddSingleton<Microsoft.Extensions.Localization.IStringLocalizer<loxxking_backend_clean.Shared.Resources.SharedResource>>(sp => sp.GetRequiredService<loxxking_backend_clean.Shared.Resources.SharedResourceLocalizer>());
        services.AddSingleton<Microsoft.Extensions.Localization.IStringLocalizerFactory, loxxking_backend_clean.Shared.Resources.SharedResourceLocalizerFactory>();

        services.Configure<RequestLocalizationOptions>(options =>
        {
            var supportedCultures = new[]
            {
                new CultureInfo("en"),
                new CultureInfo("ar")
            };

            options.DefaultRequestCulture = new RequestCulture("en");
            options.SupportedCultures = supportedCultures;
            options.SupportedUICultures = supportedCultures;
        });

        return services;
    }
}
