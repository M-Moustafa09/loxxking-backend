using loxxking_backend_clean.Domain.Entities.Users;
using loxxking_backend_clean.Domain.Enums;
using loxxking_backend_clean.Infrastructure.Persistence.Seeder.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace loxxking_backend_clean.Infrastructure.Persistence.Seeder.Users;

public class UserSeeder : IDataSeeder
{
    public int Order => 3;

    public async Task SeedAsync(SeedContext context, IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = serviceProvider.GetRequiredService<UserManager<User>>();
        var passwordHasher = serviceProvider.GetRequiredService<IPasswordHasher<User>>();
        var logger = serviceProvider.GetRequiredService<ILogger<UserSeeder>>();

        var defaultCountryId = context.DefaultCountry.Id;

        // The admin is the CRM owner account; SSO from Luxira lands on this email.
        // The three role accounts exist because the demo seeders need a customer / staff to attach data to.
        var targetUsers = new (string Name, string Email, string Phone, UserRole Role)[]
        {
            ("Super Admin", "luxiraholding@gmail.com", "+201000000001", UserRole.Admin),
            ("Store Manager", "manager@loxxking.com", "+201000000002", UserRole.StoreManager),
            ("Sales Employee", "sales@loxxking.com", "+201000000003", UserRole.SalesEmployee),
            ("Prime Customer", "customer@loxxking.com", "+201000000004", UserRole.Customer)
        };

        foreach (var (name, email, phone, role) in targetUsers)
        {
            var existingUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
            if (existingUser == null)
            {
                // One random password per account, written to the log exactly once at creation time.
                var password = GeneratePassword();
                var dummyUser = (User)Activator.CreateInstance(typeof(User), nonPublic: true)!;
                var passwordHash = passwordHasher.HashPassword(dummyUser, password);
                logger.LogWarning("SEEDED LOGIN  {Email}  password: {Password}  (save this; it is not shown again)", email, password);

                var user = User.Create(
                    name: name,
                    email: email,
                    phone: phone,
                    passwordHash: passwordHash,
                    countryId: defaultCountryId,
                    role: role,
                    preferredLanguage: "ar");

                user.SecurityStamp = Guid.NewGuid().ToString();
                user.ConcurrencyStamp = Guid.NewGuid().ToString();
                user.NormalizedEmail = email.ToUpperInvariant();
                user.NormalizedUserName = email.ToUpperInvariant();
                user.EmailConfirmed = true;
                user.PhoneNumberConfirmed = true;

                await dbContext.Users.AddAsync(user, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);

                // Ensure Identity role mapping in AspNetUserRoles
                var roleName = role.ToString();
                if (!await userManager.IsInRoleAsync(user, roleName))
                {
                    await userManager.AddToRoleAsync(user, roleName);
                }

                logger.LogInformation("Seeded user '{Email}' with role '{Role}'.", email, role);
            }
            else
            {
                if (string.IsNullOrEmpty(existingUser.SecurityStamp))
                {
                    existingUser.SecurityStamp = Guid.NewGuid().ToString();
                    existingUser.ConcurrencyStamp = Guid.NewGuid().ToString();
                    existingUser.NormalizedEmail = existingUser.Email?.ToUpperInvariant();
                    existingUser.NormalizedUserName = existingUser.UserName?.ToUpperInvariant();
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                // Ensure existing user has role in AspNetUserRoles as well
                var roleName = role.ToString();
                if (!await userManager.IsInRoleAsync(existingUser, roleName))
                {
                    await userManager.AddToRoleAsync(existingUser, roleName);
                }
            }
        }

        // Cache references in SeedContext for downstream seeders
        context.AdminUser = await dbContext.Users.FirstAsync(u => u.Email == "luxiraholding@gmail.com", cancellationToken);
        context.StoreManagerUser = await dbContext.Users.FirstAsync(u => u.Email == "manager@loxxking.com", cancellationToken);
        context.SalesEmployeeUser = await dbContext.Users.FirstAsync(u => u.Email == "sales@loxxking.com", cancellationToken);
        context.CustomerUser = await dbContext.Users.FirstAsync(u => u.Email == "customer@loxxking.com", cancellationToken);
    }

    // 16 chars from a URL-safe alphabet; long enough to be unguessable, no characters that break copy/paste.
    private static string GeneratePassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        return new string(bytes.Select(b => alphabet[b % alphabet.Length]).ToArray());
    }
}
