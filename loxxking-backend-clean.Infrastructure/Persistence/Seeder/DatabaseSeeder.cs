using loxxking_backend_clean.Infrastructure.Persistence.Seeder.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace loxxking_backend_clean.Infrastructure.Persistence.Seeder;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var scopedProvider = scope.ServiceProvider;
        var logger = scopedProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseSeeder));

        logger.LogInformation("=================================================");
        logger.LogInformation("Starting Database Seeding Process...");
        logger.LogInformation("=================================================");
        
        try
        {
            logger.LogInformation("Applying migrations before seeding...");
            var dbContext = scopedProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Migrations applied successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply migrations: {Message}", ex.Message);
            throw;
        }

        // Seeding:Skip lists seeder type names to leave out (e.g. OrderSeeder, SupportSeeder in
        // production, where their demo rows would be pushed to the CRM by the sync services).
        var skip = scopedProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>()
            ?.GetSection("Seeding:Skip").Get<string[]>() ?? Array.Empty<string>();

        var seeders = scopedProvider
            .GetServices<IDataSeeder>()
            .Where(s => !skip.Contains(s.GetType().Name, StringComparer.OrdinalIgnoreCase))
            .OrderBy(s => s.Order)
            .ToList();

        if (skip.Length > 0)
        {
            logger.LogWarning("Skipping seeders per Seeding:Skip: {Skipped}", string.Join(", ", skip));
        }

        if (seeders.Count == 0)
        {
            logger.LogWarning("No IDataSeeder implementations were registered.");
            return;
        }

        var seedContext = new SeedContext();

        foreach (var seeder in seeders)
        {
            var seederName = seeder.GetType().Name;
            logger.LogInformation("Seeding {SeederName} (Order: {Order})...", seederName, seeder.Order);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await seeder.SeedAsync(seedContext, scopedProvider, cancellationToken);
                logger.LogInformation("{SeederName} seeding completed successfully.", seederName);
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Seeding was cancelled during {SeederName}.", seederName);
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed while seeding {SeederName}. Reason: {ErrorMessage}", seederName, ex.Message);
                throw;
            }
        }

        logger.LogInformation("=================================================");
        logger.LogInformation("Database Seeding Completed Successfully.");
        logger.LogInformation("=================================================");
    }
}
