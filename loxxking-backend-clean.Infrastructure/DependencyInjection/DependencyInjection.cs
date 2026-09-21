using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using loxxking_backend_clean.Domain.Entities.Users;
using Microsoft.AspNetCore.Identity;
using loxxking_backend_clean.Infrastructure.Persistence.Seeder;

namespace loxxking_backend_clean.Infrastructure.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        services.AddIdentityCore<User>(options =>
        {
            options.User.RequireUniqueEmail = false;
        })
        .AddRoles<IdentityRole<Guid>>()
        .AddEntityFrameworkStores<ApplicationDbContext>();

        services.AddScoped<IPasswordHasher<User>, loxxking_backend_clean.Infrastructure.Authentication.LegacyBCryptPasswordHasher>();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "SmartScheme";
            options.DefaultChallengeScheme = "SmartScheme";
        })
        .AddPolicyScheme("SmartScheme", "JWT or Cookie", options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                string authorization = context.Request.Headers.Authorization.ToString();
                if (!string.IsNullOrEmpty(authorization) && authorization.StartsWith("Bearer "))
                {
                    return JwtBearerDefaults.AuthenticationScheme;
                }

                return CookieAuthenticationDefaults.AuthenticationScheme;
            };
        })
        .AddJwtBearer(options =>
        {
            var jwtSecret = configuration["Jwt:Secret"] ?? "SUPER_SECRET_KEY_NEEDS_TO_BE_LONG_ENOUGH";
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = configuration["Jwt:Issuer"] ?? "LoxxKing",
                ValidAudience = configuration["Jwt:Audience"] ?? "LoxxKingClient",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
            };
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/chatHub"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        })
        .AddCookie(options =>
        {
            options.Cookie.Name = ".Loxxking.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
            options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
            options.LoginPath = "/api/auth/login";
            options.AccessDeniedPath = "/api/auth/access-denied";

            // Neither path above is a real endpoint, so the default redirect sent API callers to the
            // SPA fallback: an unauthenticated /api call answered 200 with index.html instead of 401.
            // Answer API and SSO requests with a status code the Angular client can act on.
            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = context =>
                {
                    if (IsApiRequest(context.Request))
                    {
                        context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                },
                OnRedirectToAccessDenied = context =>
                {
                    if (IsApiRequest(context.Request))
                    {
                        context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                }
            };
        });

        services.AddMemoryCache();
        services.AddSingleton<loxxking_backend_clean.Application.Common.Interfaces.ISsoJtiValidator, loxxking_backend_clean.Infrastructure.Services.MemoryCacheSsoJtiValidator>();


        services.AddSignalR();
        
        services.AddHttpClient();
        services.AddHttpClient("LegacyCrmClient", client => 
        { 
            client.Timeout = TimeSpan.FromSeconds(configuration.GetValue<int>("LegacyCrm:TimeoutSeconds", 15)); 
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            // Accept self-signed SSL certificates for local dev CRM
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        });

        services.AddScoped<IInvoicePdfGenerator, loxxking_backend_clean.Infrastructure.Services.QuestPdfInvoiceGenerator>();
        services.AddScoped<IOrderNotificationService, loxxking_backend_clean.Infrastructure.Services.OrderNotificationService>();
        services.AddScoped<IJwtProvider, loxxking_backend_clean.Infrastructure.Authentication.JwtProvider>();
        services.AddScoped<IFileStorageService, loxxking_backend_clean.Infrastructure.Services.LocalFileStorageService>();
        
        services.AddScoped<ILegacyCrmSyncService, loxxking_backend_clean.Infrastructure.Services.LegacyCrmSyncService>();
        services.AddScoped<IIpResolverService, loxxking_backend_clean.Infrastructure.Services.IpResolverService>();
        services.AddScoped<IGeolocationService, loxxking_backend_clean.Infrastructure.Services.GeolocationService>();
        services.AddHostedService<loxxking_backend_clean.Infrastructure.Services.OrderSyncBackgroundService>();
        services.AddHostedService<loxxking_backend_clean.Infrastructure.Services.VisitorChatSyncBackgroundService>();

        // Every store visit → Luxira CRM (popup + list + email). One instance: the handler queues, the host drains.
        services.AddSingleton<loxxking_backend_clean.Infrastructure.Services.StoreVisitCrmForwarder>();
        services.AddSingleton<IStoreVisitForwarder>(sp => sp.GetRequiredService<loxxking_backend_clean.Infrastructure.Services.StoreVisitCrmForwarder>());
        services.AddHostedService(sp => sp.GetRequiredService<loxxking_backend_clean.Infrastructure.Services.StoreVisitCrmForwarder>());

        services.AddSeeders();

        return services;
    }

    // Requests the Angular client makes with fetch/XHR: they expect a status code, never a redirect
    // to a sign-in page. Everything else (a browser opening a page) keeps the normal redirect.
    private static bool IsApiRequest(Microsoft.AspNetCore.Http.HttpRequest request) =>
        request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || request.Path.StartsWithSegments("/sso", StringComparison.OrdinalIgnoreCase);
}
