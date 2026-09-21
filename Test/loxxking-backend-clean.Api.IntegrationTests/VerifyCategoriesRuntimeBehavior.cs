using loxxking_backend_clean.Application.Common.Interfaces;
using loxxking_backend_clean.Application.Features.Categories.Commands.CreateCategory;
using loxxking_backend_clean.Application.Features.Categories.Commands.DeleteCategory;
using loxxking_backend_clean.Application.Features.Categories.Commands.UpdateCategory;
using loxxking_backend_clean.Application.Features.Categories.Queries.GetCategories;
using loxxking_backend_clean.Domain.Entities.Products;
using loxxking_backend_clean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Xunit;

namespace loxxking_backend_clean.Api.IntegrationTests;

/// <summary>The dashboard's category screen: readable slugs, an image, and a delete that cannot orphan products.</summary>
public class VerifyCategoriesRuntimeBehavior
{
    private sealed class FakeStorage : IFileStorageService
    {
        public Task<string> UploadAsync(Stream fileStream, string fileName, string contentType, string folder, CancellationToken cancellationToken)
            => Task.FromResult($"/uploads/{folder}/{fileName}");

        public Task DeleteAsync(string fileUrl, CancellationToken cancellationToken) => Task.CompletedTask;
    }

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

    private static IDistributedCache GetCache()
    {
        var opts = Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryDistributedCacheOptions());
        return new MemoryDistributedCache(opts);
    }

    // A 1x1 PNG.
    private const string PngDataUrl = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public async Task Create_Gives_A_Readable_Unique_Slug_And_Stores_The_Image()
    {
        var db = GetDbContext();
        var create = new CreateCategoryHandler(db, GetCache(), new FakeStorage());

        var first = await create.Handle(new CreateCategoryCommand("Postpartum", "ما بعد الولادة", PngDataUrl), CancellationToken.None);
        var second = await create.Handle(new CreateCategoryCommand("Postpartum", "ما بعد الولادة 2"), CancellationToken.None);
        var mens = await create.Handle(new CreateCategoryCommand("Men's", "رجالي"), CancellationToken.None);

        Assert.True(first.IsSuccess && second.IsSuccess && mens.IsSuccess);
        var byId = await db.Categories.ToDictionaryAsync(c => c.Id);
        Assert.Equal("postpartum", byId[first.Value].Slug);
        Assert.Equal("postpartum-2", byId[second.Value].Slug);
        Assert.Equal("men-s", byId[mens.Value].Slug);
        Assert.StartsWith("/uploads/categories/", byId[first.Value].ImageUrl);
        Assert.EndsWith(".png", byId[first.Value].ImageUrl);
        Assert.Equal(string.Empty, byId[second.Value].ImageUrl);
    }

    [Fact]
    public async Task Update_Keeps_The_Slug_And_Handles_The_Image()
    {
        var db = GetDbContext();
        var create = new CreateCategoryHandler(db, GetCache(), new FakeStorage());
        var id = (await create.Handle(new CreateCategoryCommand("Sports", "رياضي", "https://cdn/x.png"), CancellationToken.None)).Value;
        var update = new UpdateCategoryHandler(db, GetCache(), new FakeStorage());

        // null image = keep
        Assert.True((await update.Handle(new UpdateCategoryCommand(id, "Sport", "رياضة"), CancellationToken.None)).IsSuccess);
        var cat = await db.Categories.SingleAsync(c => c.Id == id);
        Assert.Equal("sports", cat.Slug);
        Assert.Equal("Sport", cat.NameEn);
        Assert.Equal("https://cdn/x.png", cat.ImageUrl);

        // empty image = remove
        await update.Handle(new UpdateCategoryCommand(id, "Sport", "رياضة", ""), CancellationToken.None);
        Assert.Equal(string.Empty, (await db.Categories.SingleAsync(c => c.Id == id)).ImageUrl);
    }

    [Fact]
    public async Task Delete_Refuses_A_Category_With_Products_And_Soft_Deletes_Otherwise()
    {
        var db = GetDbContext();
        var cache = GetCache();
        var create = new CreateCategoryHandler(db, cache, new FakeStorage());
        var full = (await create.Handle(new CreateCategoryCommand("Women", "نساء"), CancellationToken.None)).Value;
        var withDeletedProduct = (await create.Handle(new CreateCategoryCommand("Old", "قديم"), CancellationToken.None)).Value;

        db.Products.Add(Product.Create(full, "منتج", "Prod", "", "prod-a", loxxking_backend_clean.Domain.ValueObjects.Money.FromDecimal(10m), null, new(), new(), new()));
        var gone = Product.Create(withDeletedProduct, "منتج", "Prod", "", "prod-b", loxxking_backend_clean.Domain.ValueObjects.Money.FromDecimal(10m), null, new(), new(), new());
        gone.IsDeleted = true;
        db.Products.Add(gone);
        await db.SaveChangesAsync();

        var delete = new DeleteCategoryHandler(db, cache);

        var refused = await delete.Handle(new DeleteCategoryCommand(full), CancellationToken.None);
        Assert.False(refused.IsSuccess);
        Assert.Equal("Category_HasProducts", refused.Error.Message);

        // Only a deleted product points at it: the FK is Restrict, so this must not be a real DELETE.
        var ok = await delete.Handle(new DeleteCategoryCommand(withDeletedProduct), CancellationToken.None);
        Assert.True(ok.IsSuccess);

        var list = await new GetCategoriesHandler(db, cache).Handle(new GetCategoriesQuery(), CancellationToken.None);
        Assert.DoesNotContain(list.Value, c => c.Id == withDeletedProduct);
        Assert.Contains(list.Value, c => c.Id == full);
        Assert.True((await db.Categories.IgnoreQueryFilters().SingleAsync(c => c.Id == withDeletedProduct)).IsDeleted);

        // A new category with the deleted one's name must not collide with its slug.
        var again = (await create.Handle(new CreateCategoryCommand("Old", "قديم"), CancellationToken.None)).Value;
        Assert.Equal("old-2", (await db.Categories.SingleAsync(c => c.Id == again)).Slug);
    }
}
