using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Logging;
using loxxking_backend_clean.Application.Common.Interfaces;
using loxxking_backend_clean.Infrastructure.Persistence;
using StoreUser = loxxking_backend_clean.Domain.Entities.Users.User;
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
    private readonly IJwtProvider _jwtProvider;
    private readonly UserManager<StoreUser> _userManager;

    public SsoController(
        ApplicationDbContext context,
        ISsoJtiValidator jtiValidator,
        IConfiguration config,
        ILogger<SsoController> logger,
        IJwtProvider jwtProvider,
        UserManager<StoreUser> userManager)
    {
        _userManager = userManager;
        _context = context;
        _jtiValidator = jtiValidator;
        _config = config;
        _logger = logger;
        _jwtProvider = jwtProvider;
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

        // The token is signed by the CRM, which only issues it to an account holding its Admin
        // role, so a valid signature is the proof this person is a Luxira admin. A Luxira admin
        // with no account here gets one on first sign-in. An account that already exists is never
        // created or promoted here: it goes through the checks below like any other.
        if (userInDb == null)
        {
            userInDb = await ProvisionAdminAsync(email, ct);
        }

        // Authorization checks — one generic 403 to the client for every case, so the
        // response never reveals whether the account exists or its state (G5.9).
        if (userInDb == null)
        {
            _logger.LogWarning("SSO denied: no Loxxking user for email {Email} and it could not be created.", email);
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

    // Creates the store-side Admin account for a Luxira admin signing in for the first time.
    // Country is the store's default (same rule as CreateStoreManager). The password is random and
    // never shown to anyone: this account signs in through the CRM only, and a store admin can set
    // one later with the existing change-password flow. Returns null when it cannot be created.
    private async Task<StoreUser?> ProvisionAdminAsync(string email, CancellationToken ct)
    {
        var defaultCountry = await _context.Countries.FirstOrDefaultAsync(c => c.IsDefault, ct);
        if (defaultCountry == null)
        {
            _logger.LogError("SSO auto-provision failed for {Email}: the store has no default country.", email);
            return null;
        }

        // 48 random bytes of base64 plus a fixed suffix so it always satisfies the password policy.
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)) + "aA1!";
        var name = email.Contains('@') ? email[..email.IndexOf('@')] : email;

        var user = StoreUser.Create(
            name,
            email,
            "0000000000", // User.Create requires a phone; the admin can fill in a real one later.
            "#PENDING_HASH#",
            defaultCountry.Id,
            UserRole.Admin);

        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            // Most likely two first sign-ins raced and the other one created it: read it back.
            _logger.LogWarning("SSO auto-provision for {Email} did not create a user: {Errors}",
                email, string.Join(", ", result.Errors.Select(e => e.Code)));
            return await _context.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant(), ct);
        }

        _logger.LogWarning("SSO auto-provisioned a new Admin account for {Email} (user id {UserId}).", email, user.Id);
        return user;
    }

    // The SSO hand-off above signs the admin in with the .Loxxking.Session cookie, but the Angular
    // app knows a user only by the JWT it keeps in localStorage (auth.service.ts, the HTTP
    // interceptor and the chat hub all read it). Without this endpoint the admin arrives at /admin
    // holding a valid session the SPA cannot see, so the guards bounce them to /admin/login and they
    // sign in a second time. Here the SPA trades the cookie for the same token a normal admin login
    // issues, so everything downstream behaves identically.
    //
    // Cookie scheme only: a request carrying a Bearer token already has a token and does not need one.
    [HttpGet("/api/sso/session-token")]
    [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
    public async Task<IActionResult> GetSessionToken(CancellationToken ct)
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idClaim, out var userId))
        {
            _logger.LogWarning("SSO session-token denied: session cookie has no usable user id.");
            return StatusCode(403, new { error = NotAuthorizedMessage });
        }

        // The cookie lives 12 hours, so re-check the account against the database instead of
        // trusting the claims baked in at sign-in time: it may have been deactivated, deleted or
        // demoted since (same checks, and the same generic 403, as the hand-off above — G5.9).
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null || user.IsDeleted || !user.IsActive || user.Role != UserRole.Admin)
        {
            _logger.LogWarning("SSO session-token denied for user id {UserId}: account missing or no longer an active admin.", userId);
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return StatusCode(403, new { error = NotAuthorizedMessage });
        }

        _logger.LogInformation("SSO session-token issued for {Email}.", user.Email);

        return Ok(new
        {
            token = _jwtProvider.Generate(user),
            userId = user.Id,
            role = user.Role.ToString().ToLowerInvariant(),
            name = user.Name,
            email = user.Email
        });
    }
}
