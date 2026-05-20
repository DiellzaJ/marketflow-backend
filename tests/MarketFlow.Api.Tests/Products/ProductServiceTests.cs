using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Features.Categories.DTOs;
using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Products.Services;
using MarketFlow.Application.Features.Purchases.DTOs;
using MarketFlow.Application.Features.Sales.DTOs;

namespace MarketFlow.Api.Tests.Products;

public sealed class ProductServiceTests
{
    [Fact]
    public async Task CreateProductAsync_RejectsMissingCategoryId()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var result = await service.CreateProductAsync(new CreateProductRequest
        {
            Name = "Milk",
            Barcode = "123456789"
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Category is required.", result.Message);
        Assert.False(tenantQueryService.CreateProductWasCalled);
    }

    [Fact]
    public async Task CreateProductAsync_RejectsMissingBarcode()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var result = await service.CreateProductAsync(new CreateProductRequest
        {
            Name = "Milk",
            Barcode = " ",
            CategoryId = 1
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Barcode is required.", result.Message);
        Assert.False(tenantQueryService.CreateProductWasCalled);
    }

    [Fact]
    public async Task CreateProductAsync_AcceptsValidPayload()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var result = await service.CreateProductAsync(new CreateProductRequest
        {
            Name = "Milk",
            Barcode = "123456789",
            CategoryId = 1
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Product created.", result.Message);
        Assert.True(tenantQueryService.CreateProductWasCalled);
        Assert.Equal(1, result.Data?.CategoryId);
    }

    [Fact]
    public async Task UpdateProductAsync_RejectsMissingCategoryId()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var result = await service.UpdateProductAsync(10, new UpdateProductRequest
        {
            Name = "Milk",
            Barcode = "123456789"
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Category is required.", result.Message);
        Assert.False(tenantQueryService.UpdateProductWasCalled);
    }

    [Fact]
    public async Task UpdateProductAsync_RejectsMissingBarcode()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var result = await service.UpdateProductAsync(10, new UpdateProductRequest
        {
            Name = "Milk",
            Barcode = " ",
            CategoryId = 1
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Barcode is required.", result.Message);
        Assert.False(tenantQueryService.UpdateProductWasCalled);
    }

    [Fact]
    public async Task UpdateProductAsync_AcceptsValidPayload()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var result = await service.UpdateProductAsync(10, new UpdateProductRequest
        {
            Name = "Milk",
            Barcode = "123456789",
            CategoryId = 1
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Product updated.", result.Message);
        Assert.True(tenantQueryService.UpdateProductWasCalled);
        Assert.Equal(1, result.Data?.CategoryId);
    }

    private sealed class RecordingTenantQueryService : ITenantQueryService
    {
        public bool CreateProductWasCalled { get; private set; }

        public bool UpdateProductWasCalled { get; private set; }

        public Task<IReadOnlyCollection<ProductDto>> GetProductsAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ProductDto?> GetProductAsync(int id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ProductDto> CreateProductAsync(
            CreateProductRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateProductWasCalled = true;

            return Task.FromResult(new ProductDto
            {
                Id = 10,
                Name = request.Name,
                Barcode = request.Barcode,
                CategoryId = request.CategoryId
            });
        }

        public Task<ProductDto?> UpdateProductAsync(
            int id,
            UpdateProductRequest request,
            CancellationToken cancellationToken = default)
        {
            UpdateProductWasCalled = true;

            return Task.FromResult<ProductDto?>(new ProductDto
            {
                Id = id,
                Name = request.Name,
                Barcode = request.Barcode,
                CategoryId = request.CategoryId
            });
        }

        public Task<ProductDto?> PatchProductAsync(
            int id,
            PatchProductRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteProductAsync(int id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyCollection<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyCollection<InventoryItemDto>> GetInventoryAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<InventoryItemDto> CreateInventoryItemAsync(
            CreateInventoryItemRequest request,
            int? updatedByUserId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<InventoryItemDto?> UpdateInventoryItemAsync(
            int id,
            UpdateInventoryItemRequest request,
            int? updatedByUserId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<InventoryItemDto?> PatchInventoryItemAsync(
            int id,
            PatchInventoryItemRequest request,
            int? updatedByUserId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteInventoryItemAsync(int id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyCollection<SaleDto>> GetSalesAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<SaleDto> CreateSaleAsync(
            CreateSaleRequest request,
            int createdByUserId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<SaleDto?> UpdateSaleAsync(
            int id,
            UpdateSaleRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<SaleDto?> PatchSaleAsync(
            int id,
            PatchSaleRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteSaleAsync(int id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyCollection<PurchaseDto>> GetPurchasesAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PurchaseDto> CreatePurchaseAsync(
            CreatePurchaseRequest request,
            int createdByUserId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PurchaseDto?> UpdatePurchaseAsync(
            int id,
            UpdatePurchaseRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PurchaseDto?> PatchPurchaseAsync(
            int id,
            PatchPurchaseRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeletePurchaseAsync(int id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
