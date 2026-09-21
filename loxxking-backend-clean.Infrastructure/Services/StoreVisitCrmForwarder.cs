using System.Net.Http.Json;
using System.Threading.Channels;
using loxxking_backend_clean.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace loxxking_backend_clean.Infrastructure.Services;

/// <summary>
/// Forwards every store visit to the Luxira CRM (POST api/StoreVisits/Incoming), where it becomes
/// a live popup, a «زيارات المتجر» list entry and an email (owner request 2026-09-21).
///
/// The visit request only queues (<see cref="Enqueue"/>); this background service posts one visit
/// at a time with the same key the visitor-chat sync uses. It is deliberately in-memory: a visit is
/// a notification, not a record the store must keep, so a recycle may drop the few still waiting.
/// Gate: LegacyCrm:Enabled and LegacyCrm:ForwardStoreVisits (both must be true).
/// </summary>
public sealed class StoreVisitCrmForwarder : BackgroundService, IStoreVisitForwarder
{
    // Bounded so a burst of visits (or a CRM outage) can never grow memory without limit.
    private readonly Channel<StoreVisitNotice> _queue = Channel.CreateBounded<StoreVisitNotice>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });

    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StoreVisitCrmForwarder> _logger;

    public StoreVisitCrmForwarder(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<StoreVisitCrmForwarder> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public void Enqueue(StoreVisitNotice visit)
    {
        if (!IsEnabled())
        {
            return;
        }

        _queue.Writer.TryWrite(visit);
    }

    private bool IsEnabled() =>
        _configuration.GetValue("LegacyCrm:Enabled", false)
        && _configuration.GetValue("LegacyCrm:ForwardStoreVisits", false);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var visit in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await SendAsync(visit, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Store visit was not forwarded to the CRM.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down.
        }
    }

    private async Task SendAsync(StoreVisitNotice visit, CancellationToken ct)
    {
        var baseUrl = _configuration.GetValue<string>("LegacyCrm:BaseUrl");
        var apiKey = _configuration.GetValue<string>("LegacyCrm:ChatApiKey") ?? _configuration.GetValue<string>("LegacyCrm:ApiKey");
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var client = httpClientFactory.CreateClient("LegacyCrmClient");
        client.BaseAddress = new Uri(baseUrl);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Token {apiKey}");

        var payload = new
        {
            StoreName = "Loxxking",
            visit.VisitedAtUtc,
            visit.Country,
            visit.Page,
            visit.Language,
            visit.IsNewVisitor
        };

        var response = await client.PostAsJsonAsync("api/StoreVisits/Incoming", payload, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("CRM refused a store visit: HTTP {StatusCode}.", (int)response.StatusCode);
        }
    }
}
