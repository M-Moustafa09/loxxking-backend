using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Logging;
using loxxking_backend_clean.Application.Common.Interfaces;
using loxxking_backend_clean.Infrastructure.Persistence;
using loxxking_backend_clean.Domain.Enums;

namespace loxxking_backend_clean.Api.Controllers;

[ApiController]
[Route("sso")]
public class SsoController : ControllerBase
{
    // Generic client-facing messages. The specific reason is logged server-side only,
    // so failures never leak stack traces, file paths, account existence, or account state (G5.9).
    private const string InvalidTokenMessage = "Invalid or expired sign-in link. Please return to the CRM and try again.";
    private const string NotAuthorizedMessage = "This account is not authorized to access Loxxking.";

    private readonly ApplicationDbContext _context;
    private readonly ISsoJtiValidator _jtiValidator;
    private readonly IConfiguration _config;
    private readonly ILogger<SsoController> _logger;

    public SsoController(
        ApplicationDbContext context,
        ISsoJtiValidator jtiValidator,
        IConfiguration config,
        ILogger<SsoController> logger)
    {
        _context = context;
        _jtiValidator = jtiValidator;
        _config = config;
        _logger = logger;
    }

    [HttpPost("loxxking-token")]
    [AllowAnonymous]
    public async Task<IActionResult> LaunchLoxxking([FromForm] string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("SSO rejected: token missing from the request.");
            return Unauthorized(new { error = InvalidTokenMessage });
        }

        var publicKeyPath = _config["Sso:RsaPublicKeyPath"] ?? "App_Data/Keys/sso_public_key.pem";
        var envPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), publicKeyPath);
        if (!System.IO.File.Exists(envPath))
        {
            _logger.LogError("SSO misconfiguration: public key not found at {KeyPath}.", envPath);
            return Unauthorized(new { error = InvalidTokenMessage });
        }
        
        var publicKeyPem = await System.IO.File.ReadAllTextAsync(envPath, ct);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);

        var issuer = _config["Sso:Issuer"] ?? "LuxiraCRM";
        var audience = _config["Sso:Audience"] ?? "LoxxkingApp";
        var clockSkewSeconds = _config.GetValue<int>("Sso:ClockSkewSeconds", 30);

        var tokenValidationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(clockSkewSeconds),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new RsaSecurityKey(rsa) { KeyId = Guid.NewGuid().ToString() },
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };

        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();
        ClaimsPrincipal principal;
        try
        {
            principal = handler.ValidateToken(token, tokenValidationParams, out _);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSO rejected: token validation failed.");
            return Unauthorized(new { error = InvalidTokenMessage });
        }

        var jti = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
        if (string.IsNullOrWhiteSpace(jti))
        {
            _logger.LogWarning("SSO rejected: token has no jti claim.");
            return Unauthorized(new { error = InvalidTokenMessage });
        }

        if (!_jtiValidator.TryConsume(jti, TimeSpan.FromSeconds(120)))
        {
            _logger.LogWarning("SSO rejected: token jti {Jti} was already consumed (replay).", jti);
            return Unauthorized(new { error = InvalidTokenMessage });
        }

        var email = principal.FindFirstValue(JwtRegisteredClaimNames.Email) ?? principal.FindFirstValue(System.Security.Claims.ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("SSO rejected: token has no email claim (jti {Jti}).", jti);
            return Unauthorized(new { error = InvalidTokenMessage });
        }

        // 5. DB Query using NormalizedEmail ONLY
        var normalizedEmail = email.ToUpperInvariant();
        var userInDb = await _context.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

        // Authorization checks — one generic 403 to the client for every case, so the
        // response never reveals whether the account exists or its state (G5.9).
        if (userInDb == null)
        {
            _logger.LogWarning("SSO denied: no Loxxking user for email {Email}.", email);
            return StatusCode(403, new { error = NotAuthorizedMessage });
        }
        if (userInDb.IsDeleted)
        {
            _logger.LogWarning("SSO denied: account {Email} is deleted.", email);
            return StatusCode(403, new { error = NotAuthorizedMessage });
        }
        if (!userInDb.IsActive)
        {
            _logger.LogWarning("SSO denied: account {Email} is not active.", email);
            return StatusCode(403, new { error = NotAuthorizedMessage });
        }
        if (userInDb.Role != loxxking_backend_clean.Domain.Enums.UserRole.Admin)
        {
            _logger.LogWarning("SSO denied: account {Email} has role {Role}, Admin required.", email, userInDb.Role);
            return StatusCode(403, new { error = NotAuthorizedMessage });
        }

        // 6. Issue Cookie (.Loxxking.Session)
        var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userInDb.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, userInDb.Name ?? ""));
        identity.AddClaim(new Claim(ClaimTypes.Email, userInDb.Email ?? ""));
        identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            authProperties);

        _logger.LogWarning("SSO sign-in succeeded for {Email} (jti {Jti}).", userInDb.Email, jti);

        // 7. Hardcoded 302 Local Redirect
        return LocalRedirect("/admin");
    }
}
