using loxxking_backend_clean.Application;
using loxxking_backend_clean.Infrastructure.DependencyInjection;
using loxxking_backend_clean.Infrastructure.Persistence.Seeder;
using loxxking_backend_clean.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Don't advertise the server implementation (G6.3). (IIS still adds X-Powered-By,
// which is removed at the IIS level via web.config.)
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi();

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("VisitorChatLimiter", context =>
    {
        var guestId = context.Request.Headers["X-Guest-Id"].ToString();
        var clientIp = context.Connection.RemoteIpAddress?.ToString();
        var key = !string.IsNullOrEmpty(guestId) ? guestId : (clientIp ?? "unknown");
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: key,
            factory: partition => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 15,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            });
    });
    options.RejectionStatusCode = 429;
});

var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret))
{
    throw new InvalidOperationException("CRITICAL ERROR: Jwt:Secret is missing from configuration.");
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
            "http://localhost:4200",
            "https://localhost:4200",
            "http://127.0.0.1:4200",
            "https://127.0.0.1:4200",
            "http://localhost:5050"
        )
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials();
    });
});

var app = builder.Build();

// API docs are for development only. Publishing the full OpenAPI document and the
// Scalar explorer hands an attacker the entire endpoint surface (G8.7), so gate them
// behind the Development environment.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
else
{
    // Enforce HTTPS in production (G6.1/G6.4). Behind IIS out-of-process, UseIISIntegration
    // already forwards the original scheme, so redirection resolves correctly (and no-ops
    // rather than looping if the HTTPS port can't be determined). HSTS is dev-excluded so
    // local http keeps working.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Browser security headers (G6.3). Set early so they cover static files and the SPA
// fallback too. X-Frame-Options is SAMEORIGIN (not DENY) because the dashboard previews
// the storefront in a same-origin <iframe>. CSP is Report-Only for now — it observes
// violations without breaking the Angular app; enforce it in a follow-up once the report
// is clean (and after wiring a report endpoint).
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "SAMEORIGIN";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=(), payment=()";
    headers["Content-Security-Policy-Report-Only"] =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data: https:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'self'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "object-src 'none'";
    headers.Remove("X-Powered-By");
    await next();
});

// app.UseExceptionHandler();

var localizationOptions = app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
app.UseRequestLocalization(localizationOptions);

loxxking_backend_clean.Api.Common.ResultExtensions.Configure(app.Services.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>());

app.UseCors("AllowFrontend");

app.UseRateLimiter();

// G1.1: this app now serves loxxking.com, which the CRM still hands out in customers' shipment-
// tracking links (loxxking.com/t/{code} and /Order/TrackLoxxKingShipment/{code}). Those paths have
// no route in the Angular SPA, so the router's catch-all sent customers to the home page. Forward
// them with a 301 to the CRM's PUBLIC domain, where the tracking page lives, so links sent before
// and after the domain switch both work. The base URL is config-driven (LegacyCrm:PublicTrackingBaseUrl)
// so a test store can point at the test CRM instead of production.
var crmPublicTrackingBaseUrl =
    (app.Configuration["LegacyCrm:PublicTrackingBaseUrl"] ?? "https://luxira.org").TrimEnd('/');
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var isTrackingLink =
        path.StartsWithSegments("/t", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/Order/TrackLoxxKingShipment", StringComparison.OrdinalIgnoreCase);

    if (isTrackingLink
        && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
    {
        var destination = crmPublicTrackingBaseUrl + context.Request.Path + context.Request.QueryString;
        context.Response.Headers.CacheControl = "public,max-age=3600";
        context.Response.Redirect(destination, permanent: true);
        return;
    }

    await next();
});

app.UseDefaultFiles();

// The Angular build ships every JS/CSS/media file with a content hash in its name
// (angular.json `outputHashing: "all"`, e.g. `styles-QRXD544R.css`). A given URL therefore
// never changes its bytes, so those files can be cached for a year, immutably — the browser
// stops re-validating them on every visit (G9.2). Anything WITHOUT a hash (index.html,
// favicon.ico, assets/**) may change under a stable name, so it is not cached long. This is
// fail-safe: turn hashing off and files stop matching and fall back to the short cache.
var fingerprintedFile = new System.Text.RegularExpressions.Regex(
    @"-[A-Z0-9]{8,}\.[a-zA-Z0-9]+$",
    System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var headers = ctx.Context.Response.Headers;
        if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            // index.html must always be re-fetched so a new deploy's fingerprinted names load.
            headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            headers["Pragma"] = "no-cache";
            headers["Expires"] = "0";
        }
        else if (fingerprintedFile.IsMatch(ctx.File.Name))
        {
            headers["Cache-Control"] = "public, max-age=31536000, immutable";
        }
        else
        {
            // Non-fingerprinted (favicon, assets/**, licenses): cache briefly so an edit under
            // the same name still reaches visitors within a day.
            headers["Cache-Control"] = "public, max-age=86400";
        }
    }
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<loxxking_backend_clean.Api.Hubs.ChatHub>("/chatHub");

app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
        ctx.Context.Response.Headers.Append("Pragma", "no-cache");
        ctx.Context.Response.Headers.Append("Expires", "0");
    }
});

if (args.Contains("--seed"))
{
    await app.Services.SeedDatabaseAsync();
    if (args.Length == 1 && args[0] == "--seed")
    {
        return;
    }
}

app.Run();
public partial class Program { }
