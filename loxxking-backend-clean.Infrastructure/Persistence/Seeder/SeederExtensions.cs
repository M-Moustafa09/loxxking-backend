using loxxking_backend_clean.Infrastructure.Persistence.Seeder.Categories;
using loxxking_backend_clean.Infrastructure.Persistence.Seeder.Common;
using loxxking_backend_clean.Infrastructure.Persistence.Seeder.Configs;
using loxxking_backend_clean.Infrastructure.Persistence.Seeder.Countries;
using loxxking_backend_clean.Infrastructure.Persistence.Seeder.Favorites;
using loxxking_backend_clean.Infrastructure.Persistence.Seeder.Notifications;
using loxxking_backend_clean.Infrastructure.Persistence.Seeder.Orders;
using loxxking_backend_clean.Infrastructure.Persistence.Seeder.Products;
using loxxking_backend_clean.Infrastructure.Persistence.Seeder.Reviews;
using loxxking_backend_clean.Infrastructure.Persistence.Seeder.Roles;
using loxxking_backend_clean.Infrastructure.Persistence.Seeder.SiteVisits;
using loxxking_backend_clean.Infrastructure.Persistence.Seeder.Support;
using loxxking_backend_clean.Infrastructure.Persistence.Seeder.Users;
using Microsoft.Extensions.DependencyInjection;

namespace loxxking_backend_clean.Infrastructure.Persistence.Seeder;

public static class SeederExtensions
{
    public static IServiceCollection AddSeeders(this IServiceCollection services)
    {
        services.AddScoped<IDataSeeder, RoleSeeder>();
        services.AddScoped<IDataSeeder, CountrySeeder>();
        services.AddScoped<IDataSeeder, UserSeeder>();
        services.AddScoped<IDataSeeder, CategorySeeder>();
        services.AddScoped<IDataSeeder, ProductSeeder>();
        services.AddScoped<IDataSeeder, PageConfigSeeder>();
        services.AddScoped<IDataSeeder, OrderSeeder>();
        services.AddScoped<IDataSeeder, ReviewSeeder>();
        services.AddScoped<IDataSeeder, FavoriteSeeder>();
        services.AddScoped<IDataSeeder, NotificationSeeder>();
        services.AddScoped<IDataSeeder, SupportSeeder>();
        services.AddScoped<IDataSeeder, SiteVisitSeeder>();

        return services;
    }

    public static async Task SeedDatabaseAsync(
        this IServiceProvider serviceProvider, 
        CancellationToken cancellationToken = default)
    {
        await DatabaseSeeder.SeedAsync(serviceProvider, cancellationToken);
    }
}
