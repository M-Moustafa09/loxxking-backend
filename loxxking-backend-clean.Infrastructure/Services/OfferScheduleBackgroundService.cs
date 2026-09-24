using loxxking_backend_clean.Application.Common.Interfaces;
using loxxking_backend_clean.Application.Features.Offers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace loxxking_backend_clean.Infrastructure.Services;

/// <summary>
/// Tells the open storefronts when a scheduled offer starts or ends (see <see cref="OfferSchedule"/>),
/// within <see cref="Interval"/> of the moment, so the discount appears and disappears without a refresh.
/// </summary>
public class OfferScheduleBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OfferScheduleBackgroundService> _logger;

    public OfferScheduleBackgroundService(IServiceProvider serviceProvider, ILogger<OfferScheduleBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Pages opened before the app started load fresh data when they reconnect, so the first
        // window starts now.
        var checkedUpTo = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var now = DateTime.UtcNow;
            try
            {
                using var scope = _serviceProvider.CreateScope();
                await OfferSchedule.PublishStartsAndEndsAsync(
                    scope.ServiceProvider.GetRequiredService<IApplicationDbContext>(),
                    scope.ServiceProvider.GetRequiredService<IDistributedCache>(),
                    scope.ServiceProvider.GetRequiredService<ICatalogChangeNotifier>(),
                    checkedUpTo, now, stoppingToken);
                checkedUpTo = now;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                // The window is kept, so the next run covers these seconds too.
                _logger.LogError(ex, "Could not publish the offers that started or ended.");
            }
        }
    }
}
