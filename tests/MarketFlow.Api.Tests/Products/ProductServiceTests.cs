using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
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
    public async Task GetProductsAsync_RejectsInvalidPage()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var result = await service.GetProductsAsync(new ProductListQuery
        {
            Page = 0
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Page must be greater than zero.", result.Message);
        Assert.False(tenantQueryService.GetProductsWasCalled);
    }

    [Fact]
    public async Task GetProductsAsync_RejectsUnsupportedSortField()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var result = await service.GetProductsAsync(new ProductListQuery
        {
            SortBy = "drop table products"
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Sort field is not supported.", result.Message);
        Assert.False(tenantQueryService.GetProductsWasCalled);
    }

    [Fact]
    public async Task GetProductsAsync_TrimsSortValuesBeforeValidation()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var query = new ProductListQuery
        {
            SortBy = " barcode ",
            SortDirection = " desc "
        };

        var result = await service.GetProductsAsync(query);

        Assert.True(result.Succeeded);
        Assert.True(tenantQueryService.GetProductsWasCalled);
        Assert.Equal("barcode", tenantQueryService.LastProductListQuery?.SortBy);
        Assert.Equal("desc", tenantQueryService.LastProductListQuery?.SortDirection);
    }

    [Fact]
    public async Task GetProductsAsync_PassesValidQueryToTenantQueryService()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var query = new ProductListQuery
        {
            Search = "milk",
            CategoryId = 2,
            IsActive = true,
            Page = 2,
            PageSize = 10,
            SortBy = "barcode",
            SortDirection = "desc"
        };

        var result = await service.GetProductsAsync(query);

        Assert.True(result.Succeeded);
        Assert.True(tenantQueryService.GetProductsWasCalled);
        Assert.Same(query, tenantQueryService.LastProductListQuery);
        Assert.Equal(2, result.Data?.Page);
        Assert.Equal(10, result.Data?.PageSize);
        Assert.Equal(1, result.Data?.TotalCount);
    }

    [Fact]
    public async Task CreateProductAsync_RejectsMissingName()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var result = await service.CreateProductAsync(new CreateProductRequest
        {
            Name = " ",
            Barcode = "123456789",
            CategoryId = 1
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Product name is required.", result.Message);
        Assert.False(tenantQueryService.CreateProductWasCalled);
    }

    [Fact]
    public async Task CreateProductAsync_RejectsNameOverMaximumLength()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var result = await service.CreateProductAsync(new CreateProductRequest
        {
            Name = new string('M', 201),
            Barcode = "123456789",
            CategoryId = 1
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Product name cannot exceed 200 characters.", result.Message);
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
    public async Task CreateProductAsync_RejectsNegativeUnitPrice()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var result = await service.CreateProductAsync(new CreateProductRequest
        {
            Name = "Milk",
            Barcode = "123456789",
            UnitPrice = -0.01m,
            CategoryId = 1
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Unit price cannot be negative.", result.Message);
        Assert.False(tenantQueryService.CreateProductWasCalled);
    }

    [Fact]
    public async Task CreateProductAsync_RejectsNegativeCostPrice()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var result = await service.CreateProductAsync(new CreateProductRequest
        {
            Name = "Milk",
            Barcode = "123456789",
            CostPrice = -0.01m,
            CategoryId = 1
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Cost price cannot be negative.", result.Message);
        Assert.False(tenantQueryService.CreateProductWasCalled);
    }

    [Fact]
    public async Task CreateProductAsync_RejectsDuplicateBarcode()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        tenantQueryService.ExistingBarcodes.Add("123456789");
        var service = new ProductService(tenantQueryService);

        var result = await service.CreateProductAsync(new CreateProductRequest
        {
            Name = "Milk",
            Barcode = "123456789",
            CategoryId = 1
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.Conflict, result.FailureType);
        Assert.Equal("Barcode is already used by another product.", result.Message);
        Assert.False(tenantQueryService.CreateProductWasCalled);
    }

    [Fact]
    public async Task CreateProductAsync_RejectsUnknownCategory()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        tenantQueryService.ExistingCategoryIds.Clear();
        var service = new ProductService(tenantQueryService);

        var result = await service.CreateProductAsync(new CreateProductRequest
        {
            Name = "Milk",
            Barcode = "123456789",
            CategoryId = 1
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Category was not found.", result.Message);
        Assert.False(tenantQueryService.CreateProductWasCalled);
    }

    [Fact]
    public async Task CreateProductAsync_AcceptsValidPayloadWithoutCategory()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var result = await service.CreateProductAsync(new CreateProductRequest
        {
            Name = " Milk ",
            Barcode = " 123456789 ",
            UnitPrice = 1.25m,
            CostPrice = 0.75m
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Product created.", result.Message);
        Assert.True(tenantQueryService.CreateProductWasCalled);
        Assert.Null(result.Data?.CategoryId);
        Assert.Equal("Milk", result.Data?.Name);
        Assert.Equal("123456789", result.Data?.Barcode);
    }

    [Fact]
    public async Task UpdateProductAsync_RejectsUnknownCategory()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        tenantQueryService.ExistingCategoryIds.Clear();
        var service = new ProductService(tenantQueryService);

        var result = await service.UpdateProductAsync(10, new UpdateProductRequest
        {
            Name = "Milk",
            Barcode = "123456789",
            CategoryId = 1
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Category was not found.", result.Message);
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
    public async Task UpdateProductAsync_AllowsCurrentProductBarcode()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        tenantQueryService.ExistingBarcodes.Add("123456789");
        tenantQueryService.BarcodeOwnerIds["123456789"] = 10;
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

    [Fact]
    public async Task UpdateProductAsync_RejectsDuplicateBarcodeFromAnotherProduct()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        tenantQueryService.ExistingBarcodes.Add("123456789");
        tenantQueryService.BarcodeOwnerIds["123456789"] = 11;
        var service = new ProductService(tenantQueryService);

        var result = await service.UpdateProductAsync(10, new UpdateProductRequest
        {
            Name = "Milk",
            Barcode = "123456789",
            CategoryId = 1
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.Conflict, result.FailureType);
        Assert.Equal("Barcode is already used by another product.", result.Message);
        Assert.False(tenantQueryService.UpdateProductWasCalled);
    }

    private sealed class RecordingTenantQueryService : ITenantQueryService
    {
        public bool CreateProductWasCalled { get; private set; }

        public bool UpdateProductWasCalled { get; private set; }

        public bool GetProductsWasCalled { get; private set; }

        public ProductListQuery? LastProductListQuery { get; private set; }

        public HashSet<int> ExistingCategoryIds { get; } = [1];

        public HashSet<string> ExistingBarcodes { get; } = [];

        public Dictionary<string, int> BarcodeOwnerIds { get; } = [];

        public Task<PagedResult<ProductDto>> GetProductsAsync(
            ProductListQuery query,
            CancellationToken cancellationToken = default)
        {
            GetProductsWasCalled = true;
            LastProductListQuery = query;

            return Task.FromResult(new PagedResult<ProductDto>
            {
                Items =
                [
                    new ProductDto
                    {
                        Id = 10,
                        Name = "Milk",
                        Barcode = "123456789",
                        CategoryId = 2,
                        IsActive = true
                    }
                ],
                Page = query.Page,
                PageSize = query.PageSize,
                TotalCount = 1,
                TotalPages = 1
            });
        }

        public Task<ProductDto?> GetProductAsync(int id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> CategoryExistsAsync(int categoryId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistingCategoryIds.Contains(categoryId));
        }

        public Task<bool> ProductBarcodeExistsAsync(
            string barcode,
            int? excludedProductId = null,
            CancellationToken cancellationToken = default)
        {
            var exists = ExistingBarcodes.Contains(barcode);

            if (exists &&
                excludedProductId.HasValue &&
                BarcodeOwnerIds.TryGetValue(barcode, out var ownerId) &&
                ownerId == excludedProductId.Value)
            {
                exists = false;
            }

            return Task.FromResult(exists);
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
