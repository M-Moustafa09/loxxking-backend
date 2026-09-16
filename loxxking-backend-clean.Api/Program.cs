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

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
            ctx.Context.Response.Headers.Append("Pragma", "no-cache");
            ctx.Context.Response.Headers.Append("Expires", "0");
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
