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

    private static string BuildProductWhereClause(ProductListQuery query)
    {
        var method = typeof(TenantQueryService).GetMethod(
            "BuildProductWhereClause",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        return Assert.IsType<string>(method.Invoke(null, [query]));
    }
}
