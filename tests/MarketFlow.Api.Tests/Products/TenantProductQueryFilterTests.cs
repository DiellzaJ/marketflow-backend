using System.Reflection;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Infrastructure.Persistence;

namespace MarketFlow.Api.Tests.Products;

public sealed class TenantProductQueryFilterTests
{
    [Fact]
    public void ProductListFilter_HidesInactiveProductsByDefault()
    {
        var whereClause = BuildProductWhereClause(new ProductListQuery());

        Assert.Contains("p.is_active = TRUE", whereClause, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductListFilter_WhenIncludeInactiveIsTrue_DoesNotFilterByActiveState()
    {
        var whereClause = BuildProductWhereClause(new ProductListQuery
        {
            IncludeInactive = true
        });

        Assert.DoesNotContain("p.is_active", whereClause, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductListFilter_WhenIsActiveIsFalse_ReturnsInactiveProducts()
    {
        var whereClause = BuildProductWhereClause(new ProductListQuery
        {
            IsActive = false
        });

        Assert.Contains("p.is_active = @is_active", whereClause, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductListFilter_UsesPartialBarcodeMatch()
    {
        var whereClause = BuildProductWhereClause(new ProductListQuery
        {
            Barcode = "123"
        });

        Assert.Contains("p.barcode ILIKE @barcode", whereClause, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductBarcodeCacheVariant_IncludesPaginationAndSort()
    {
        var firstVariant = CreateProductBarcodeLookupVariantKey(new ProductListQuery
        {
            Barcode = "123",
            Page = 1,
            PageSize = 1,
            SortBy = "name",
            SortDirection = "asc"
        });
        var secondVariant = CreateProductBarcodeLookupVariantKey(new ProductListQuery
        {
            Barcode = "123",
            Page = 1,
            PageSize = 20,
            SortBy = "barcode",
            SortDirection = "desc"
        });

        Assert.NotEqual(firstVariant, secondVariant);
    }

    private static string BuildProductWhereClause(ProductListQuery query)
    {
        var method = typeof(TenantQueryService).GetMethod(
            "BuildProductWhereClause",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        return Assert.IsType<string>(method.Invoke(null, [query]));
    }

    private static string CreateProductBarcodeLookupVariantKey(ProductListQuery query)
    {
        var method = typeof(TenantQueryService).GetMethod(
            "CreateProductBarcodeLookupVariantKey",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        return Assert.IsType<string>(method.Invoke(null, [query]));
    }
}
