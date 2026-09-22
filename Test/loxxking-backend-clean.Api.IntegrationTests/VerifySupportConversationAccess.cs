using loxxking_backend_clean.Application.Features.Support.Queries.GetMessages;
using loxxking_backend_clean.Domain.Entities.Countries;
using loxxking_backend_clean.Domain.Entities.Support;
using loxxking_backend_clean.Domain.Entities.Users;
using loxxking_backend_clean.Domain.Enums;
using loxxking_backend_clean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace loxxking_backend_clean.Api.IntegrationTests;

/// <summary>A support conversation is read only by its customer or by staff — the id alone is not enough.</summary>
public class VerifySupportConversationAccess
{
    private static ApplicationDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        var context = new ApplicationDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task Messages_Are_Read_Only_By_Their_Guest_Their_Customer_Or_Staff()
    {
        var db = GetDbContext();
        var country = Country.Create("Egypt", "EGP");
        db.Countries.Add(country);
        var customer = User.Create("Mona", "mona@test.com", "1", "hash", country.Id, UserRole.Customer, "ar");
        var stranger = User.Create("Other", "other@test.com", "2", "hash", country.Id, UserRole.Customer, "ar");
        db.Users.AddRange(customer, stranger);

        var guestId = Guid.NewGuid().ToString();
        var guestChat = SupportConversation.Create($"guest:{guestId}", "Guest", "", $"guest_{guestId}@guest.local");
        guestChat.AddMessage(null, null, "hello", null, null, null, "Guest");
        var customerChat = SupportConversation.Create("", "Mona", "1", "mona@test.com");
        db.SupportConversations.AddRange(guestChat, customerChat);
        await db.SaveChangesAsync();

        var handler = new GetMessagesHandler(db);
        async Task<bool> CanRead(Guid chat, Guid? userId, string? guest, bool staff)
            => (await handler.Handle(new GetMessagesQuery(chat, userId, guest, staff), CancellationToken.None)).IsSuccess;

        Assert.True(await CanRead(guestChat.Id, null, guestId, false));
        Assert.True(await CanRead(guestChat.Id, null, null, true));
        Assert.True(await CanRead(customerChat.Id, customer.Id, null, false));

        Assert.False(await CanRead(guestChat.Id, null, null, false));
        Assert.False(await CanRead(guestChat.Id, null, Guid.NewGuid().ToString(), false));
        Assert.False(await CanRead(customerChat.Id, stranger.Id, null, false));
        Assert.False(await CanRead(Guid.NewGuid(), null, null, true));
    }
}
