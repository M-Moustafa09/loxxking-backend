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
