using MarketFlow.Api.Controllers;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Products.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Tests.Products;

public sealed class ProductsControllerTests
{
    [Fact]
    public async Task GetAsync_ReturnsOkWithPagedProducts()
    {
        var product = new ProductDto
        {
            Id = 42,
            Name = "Milk",
            CategoryId = 1
        };

        var query = new ProductListQuery
        {
            Search = "milk",
            Page = 2,
            PageSize = 10
        };

        var controller = new ProductsController(new StubProductService(product));

        var response = await controller.GetAsync(query, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var result = Assert.IsType<ServiceResult<PagedResult<ProductDto>>>(ok.Value);
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data?.Page);
        Assert.Equal(10, result.Data?.PageSize);
        Assert.Same(product, Assert.Single(result.Data!.Items));
    }

    [Fact]
    public async Task CreateAsync_ReturnsCreatedAtNamedProductRoute()
    {
        var product = new ProductDto
        {
            Id = 42,
            Name = "Milk",
            CategoryId = 1
        };

        var controller = new ProductsController(new StubProductService(product));

        var response = await controller.CreateAsync(
            new CreateProductRequest
            {
                Name = "Milk",
                CategoryId = 1
            },
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtRouteResult>(response.Result);
        Assert.Equal("GetProductById", created.RouteName);
        Assert.Equal(product.Id, created.RouteValues?["id"]);
        Assert.Same(product, Assert.IsType<ServiceResult<ProductDto>>(created.Value).Data);
    }

    private sealed class StubProductService : IProductService
    {
        private readonly ProductDto _product;

        public StubProductService(ProductDto product)
        {
            _product = product;
        }

        public Task<ServiceResult<PagedResult<ProductDto>>> GetProductsAsync(
            ProductListQuery query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ServiceResult<PagedResult<ProductDto>>.Success(new PagedResult<ProductDto>
            {
                Items = [_product],
                Page = query.Page,
                PageSize = query.PageSize,
                TotalCount = 1,
                TotalPages = 1
            }));
        }

        public Task<ServiceResult<ProductDto>> GetProductAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ServiceResult<ProductDto>> CreateProductAsync(
            CreateProductRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ServiceResult<ProductDto>.Success(_product, "Product created."));
        }

        public Task<ServiceResult<ProductDto>> UpdateProductAsync(
            int id,
            UpdateProductRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ServiceResult<ProductDto>> PatchProductAsync(
            int id,
            PatchProductRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ServiceResult<bool>> DeleteProductAsync(int id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
