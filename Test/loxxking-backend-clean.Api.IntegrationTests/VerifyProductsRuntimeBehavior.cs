using loxxking_backend_clean.Application.Features.Products.Commands.CreateProduct;
using loxxking_backend_clean.Application.Features.Products.Commands.UpdateProduct;
using loxxking_backend_clean.Application.Features.Products.Queries.GetProduct;
using loxxking_backend_clean.Domain.Entities.Categories;
using loxxking_backend_clean.Domain.Entities.Inventory;
using loxxking_backend_clean.Domain.Entities.Products;
using loxxking_backend_clean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Xunit;

namespace loxxking_backend_clean.Api.IntegrationTests;

public class VerifyProductsRuntimeBehavior
{
    private ApplicationDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        var context = new ApplicationDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }

    private IDistributedCache GetCache()
    {
        var opts = Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryDistributedCacheOptions());
        return new Microsoft.Extensions.Caching.Distributed.MemoryDistributedCache(opts);
    }

    [Fact]
    public async Task Verify_Product_Create_Read_Update()
    {
        var db = GetDbContext();
        
        var category = Category.Create("CatAr", "CatEn", "cat-en", "img");
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var createHandler = new CreateProductHandler(db, null!);
        var createCmd = new CreateProductCommand(
            category.Id,
            "ProdAr",
            "ProdEn",
            "Desc",
            new List<string> { "img1" },
            null,
            null,
            null,
            150m
        );
        
        var createResult = await createHandler.Handle(createCmd, CancellationToken.None);
        Assert.True(createResult.IsSuccess);
        
        var getHandler = new GetProductHandler(db, GetCache());
        var getResult = await getHandler.Handle(new GetProductQuery(createResult.Value.Id), CancellationToken.None);
        
        Assert.True(getResult.IsSuccess);
        Assert.Equal(150m, getResult.Value.Price);
        Assert.Null(getResult.Value.OriginalPrice);
        Assert.StartsWith("proden-", getResult.Value.Slug);
        
        var updateHandler = new UpdateProductHandler(db, null!);
        var badUpdateCmd = new UpdateProductCommand(
            createResult.Value.Id,
            "ProdAr Updated",
            "ProdEn Updated",
            "Desc",
            new List<string>(),
            null,
            null,
            null,
            -50m // Invalid negative base price
        );
        
        await Assert.ThrowsAsync<ArgumentException>(() => updateHandler.Handle(badUpdateCmd, CancellationToken.None));
        
        var validUpdateCmd = new UpdateProductCommand(
            createResult.Value.Id,
            "ProdAr Updated",
            "ProdEn Updated",
            "Desc",
            new List<string>(),
            null,
            null,
            null,
            120m
        );
        
        var updateResult = await updateHandler.Handle(validUpdateCmd, CancellationToken.None);
        Assert.True(updateResult.IsSuccess);
        
        var getUpdatedResult = await getHandler.Handle(new GetProductQuery(createResult.Value.Id), CancellationToken.None);
        Assert.Equal(120m, getUpdatedResult.Value.Price);
        Assert.Null(getUpdatedResult.Value.OriginalPrice);
    }
}
