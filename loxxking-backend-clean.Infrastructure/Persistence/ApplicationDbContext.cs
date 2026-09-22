using loxxking_backend_clean.Domain.Entities.Categories;
using loxxking_backend_clean.Domain.Entities.Products;
using loxxking_backend_clean.Domain.Entities.Countries;
using loxxking_backend_clean.Domain.Entities.BankTransfers;
using loxxking_backend_clean.Domain.Entities.Inventory;
using loxxking_backend_clean.Domain.Entities.Invoices;
using loxxking_backend_clean.Domain.Entities.Notifications;
using loxxking_backend_clean.Domain.Entities.Offers;
using loxxking_backend_clean.Domain.Entities.Orders;
using loxxking_backend_clean.Domain.Entities.Reviews;
using loxxking_backend_clean.Domain.Entities.SiteVisits;
using loxxking_backend_clean.Domain.Entities.Support;
using loxxking_backend_clean.Domain.Entities.Users;
using loxxking_backend_clean.Domain.Entities.Favorites;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System.Reflection;

namespace loxxking_backend_clean.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<BankTransfer> BankTransfers => Set<BankTransfer>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<BundleOffer> BundleOffers { get; set; }
    public DbSet<BundleOfferItem> BundleOfferItems { get; set; }
    public DbSet<OffersPageConfig> OffersPageConfigs { get; set; }
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<OfferProduct> OfferProducts => Set<OfferProduct>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderEditLog> OrderEditLogs => Set<OrderEditLog>();
    public DbSet<ProductPrice> ProductPrices => Set<ProductPrice>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<SiteVisit> SiteVisits => Set<SiteVisit>();
    public DbSet<SupportConversation> SupportConversations => Set<SupportConversation>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public new DbSet<User> Users => Set<User>();
    public DbSet<FavoriteItem> FavoriteItems { get; set; }
    public DbSet<FavoritesPageConfig> FavoritesPageConfigs => Set<FavoritesPageConfig>();
    public DbSet<loxxking_backend_clean.Domain.Entities.HomePage.HomePageConfig> HomePageConfigs => Set<loxxking_backend_clean.Domain.Entities.HomePage.HomePageConfig>();
    public DbSet<loxxking_backend_clean.Domain.Entities.PageConfigurations.PageConfiguration> PageConfigurations => Set<loxxking_backend_clean.Domain.Entities.PageConfigurations.PageConfiguration>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<loxxking_backend_clean.Domain.Entities.PageConfigurations.PageConfiguration>(b =>
        {
            b.ToTable("PageConfigurations");
            b.HasKey(c => c.Key);
            b.Property(c => c.Key).HasMaxLength(100).IsRequired();
            b.Property(c => c.ConfigJson).IsRequired();
            b.Property(c => c.UpdatedAt).IsRequired();
        });

        builder.Entity<User>(b =>
        {
            b.ToTable("Users");
            b.Property(u => u.PhoneNumber).HasColumnName("Phone").IsRequired();
            b.Property(u => u.Email).HasColumnName("Email").IsRequired();
            b.Property(u => u.PasswordHash).HasColumnName("PasswordHash").IsRequired();
            
            b.Property(u => u.EmailConfirmed).HasDefaultValue(true);
            b.Property(u => u.PhoneNumberConfirmed).HasDefaultValue(true);
            
            b.HasIndex(u => u.Role).HasFilter("[Role] != 3");
        });

        builder.Entity<SupportMessage>(b =>
        {
            b.HasIndex(m => new { m.ConversationId, m.IsRead });
        });

        builder.Entity<FavoriteItem>(b =>
        {
            b.HasIndex(f => f.GuestId);
        });

        builder.Entity<SiteVisit>(b =>
        {
            // Note: The index on (VisitedAt, CountryId) is deferred pending an async write mechanism 
            // (queue/buffer) for high-frequency inserts to avoid slowing down writes.
        });

        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Not the owned values (an offer's or a bundle's ActivePeriod): they live in their owner's row, and
        // Restrict on them made every offer and bundle delete throw before it reached the database.
        foreach (var relationship in builder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()).Where(fk => !fk.IsOwnership))
        {
            relationship.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}
