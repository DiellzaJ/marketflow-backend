using MarketFlow.Api.Controllers;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Products.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Tests.Products;

public sealed class ProductsControllerTests
{
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
        Assert.Equal(nameof(ProductsController.GetByIdAsync), created.RouteName);
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

        public Task<ServiceResult<IReadOnlyCollection<ProductDto>>> GetProductsAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
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
