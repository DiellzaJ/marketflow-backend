using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Categories.DTOs;
using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Products.Exceptions;
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
            IsActive = false,
            IncludeInactive = true,
            Page = 2,
            PageSize = 10,
            SortBy = "barcode",
            SortDirection = "desc"
        };

        var result = await service.GetProductsAsync(query);

        Assert.True(result.Succeeded);
        Assert.True(tenantQueryService.GetProductsWasCalled);
        Assert.Same(query, tenantQueryService.LastProductListQuery);
        Assert.False(tenantQueryService.LastProductListQuery?.IsActive);
        Assert.True(tenantQueryService.LastProductListQuery?.IncludeInactive);
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
        Assert.Equal(ServiceResultFailureType.NotFound, result.FailureType);
        Assert.Equal("Category was not found.", result.Message);
        Assert.False(tenantQueryService.CreateProductWasCalled);
    }

    [Fact]
    public async Task CreateProductAsync_WhenDatabaseReportsDuplicateBarcode_ReturnsConflict()
    {
        var tenantQueryService = new RecordingTenantQueryService
        {
            ThrowBarcodeConflictOnCreate = true
        };
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
        Assert.True(tenantQueryService.CreateProductWasCalled);
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
    public async Task GetProductAsync_WhenProductIsInactive_ReturnsNotFoundByDefault()
    {
        var tenantQueryService = new RecordingTenantQueryService
        {
            CurrentProduct = new ProductDto
            {
                Id = 10,
                Name = "Milk",
                Barcode = "123456789",
                CategoryId = 1,
                UnitPrice = 1.25m,
                CostPrice = 0.75m,
                IsActive = false
            }
        };
        var service = new ProductService(tenantQueryService);

        var result = await service.GetProductAsync(10);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.NotFound, result.FailureType);
        Assert.Equal("Product was not found.", result.Message);
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
        Assert.Equal(ServiceResultFailureType.NotFound, result.FailureType);
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

    [Fact]
    public async Task UpdateProductAsync_WhenDatabaseReportsDuplicateBarcode_ReturnsConflict()
    {
        var tenantQueryService = new RecordingTenantQueryService
        {
            ThrowBarcodeConflictOnUpdate = true
        };
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
        Assert.True(tenantQueryService.UpdateProductWasCalled);
    }

    [Fact]
    public async Task PatchProductAsync_WhenProductDoesNotExist_ReturnsNotFound()
    {
        var tenantQueryService = new RecordingTenantQueryService
        {
            CurrentProduct = null
        };
        var service = new ProductService(tenantQueryService);

        var result = await service.PatchProductAsync(10, new PatchProductRequest
        {
            Name = "Milk"
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.NotFound, result.FailureType);
        Assert.Equal("Product was not found.", result.Message);
        Assert.False(tenantQueryService.PatchProductWasCalled);
    }

    [Fact]
    public async Task PatchProductAsync_RejectsNegativeUnitPrice()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var result = await service.PatchProductAsync(10, new PatchProductRequest
        {
            UnitPrice = -0.01m
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Unit price cannot be negative.", result.Message);
        Assert.False(tenantQueryService.PatchProductWasCalled);
    }

    [Fact]
    public async Task PatchProductAsync_RejectsDuplicateBarcodeFromAnotherProduct()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        tenantQueryService.ExistingBarcodes.Add("987654321");
        tenantQueryService.BarcodeOwnerIds["987654321"] = 11;
        var service = new ProductService(tenantQueryService);

        var result = await service.PatchProductAsync(10, new PatchProductRequest
        {
            Barcode = " 987654321 "
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.Conflict, result.FailureType);
        Assert.Equal("Barcode is already used by another product.", result.Message);
        Assert.False(tenantQueryService.PatchProductWasCalled);
    }

    [Fact]
    public async Task PatchProductAsync_RejectsUnknownCategory()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        tenantQueryService.ExistingCategoryIds.Clear();
        var service = new ProductService(tenantQueryService);

        var result = await service.PatchProductAsync(10, new PatchProductRequest
        {
            CategoryId = 2
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.NotFound, result.FailureType);
        Assert.Equal("Category was not found.", result.Message);
        Assert.False(tenantQueryService.PatchProductWasCalled);
    }

    [Fact]
    public async Task PatchProductAsync_WhenDatabaseReportsDuplicateBarcode_ReturnsConflict()
    {
        var tenantQueryService = new RecordingTenantQueryService
        {
            ThrowBarcodeConflictOnPatch = true
        };
        var service = new ProductService(tenantQueryService);

        var result = await service.PatchProductAsync(10, new PatchProductRequest
        {
            Barcode = "123456789"
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.Conflict, result.FailureType);
        Assert.Equal("Barcode is already used by another product.", result.Message);
        Assert.True(tenantQueryService.PatchProductWasCalled);
    }

    [Fact]
    public async Task PatchProductAsync_AcceptsValidPatchAndTrimsValues()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var result = await service.PatchProductAsync(10, new PatchProductRequest
        {
            Name = " Oat Milk ",
            Barcode = " 987654321 ",
            UnitPrice = 1.50m
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Product updated.", result.Message);
        Assert.True(tenantQueryService.PatchProductWasCalled);
        Assert.Equal("Oat Milk", result.Data?.Name);
        Assert.Equal("987654321", result.Data?.Barcode);
        Assert.Equal(1.50m, result.Data?.UnitPrice);
    }

    [Fact]
    public async Task PatchProductAsync_RejectsActiveStateChange()
    {
        var tenantQueryService = new RecordingTenantQueryService
        {
            CurrentProduct = new ProductDto
            {
                Id = 10,
                Name = "Milk",
                Barcode = "123456789",
                CategoryId = 1,
                UnitPrice = 1.25m,
                CostPrice = 0.75m,
                IsActive = false
            }
        };
        var service = new ProductService(tenantQueryService);

        var result = await service.PatchProductAsync(10, new PatchProductRequest
        {
            IsActive = true
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Use the product deactivate or reactivate endpoint to change active state.", result.Message);
        Assert.False(tenantQueryService.PatchProductWasCalled);
    }

    [Fact]
    public async Task UpdateProductAsync_RejectsActiveStateChange()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var result = await service.UpdateProductAsync(10, new UpdateProductRequest
        {
            Name = "Milk",
            Barcode = "123456789",
            CategoryId = 1,
            IsActive = false
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Use the product deactivate or reactivate endpoint to change active state.", result.Message);
        Assert.False(tenantQueryService.UpdateProductWasCalled);
    }

    [Fact]
    public async Task DeleteProductAsync_DeactivatesProduct()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var result = await service.DeleteProductAsync(10);

        Assert.True(result.Succeeded);
        Assert.True(result.Data);
        Assert.Equal("Product deactivated.", result.Message);
        Assert.True(tenantQueryService.SetProductActiveStateWasCalled);
        Assert.False(tenantQueryService.LastActiveState);
    }

    [Fact]
    public async Task DeleteProductAsync_WhenProductDoesNotExist_ReturnsNotFound()
    {
        var tenantQueryService = new RecordingTenantQueryService
        {
            CurrentProduct = null
        };
        var service = new ProductService(tenantQueryService);

        var result = await service.DeleteProductAsync(10);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.NotFound, result.FailureType);
        Assert.Equal("Product was not found.", result.Message);
        Assert.False(tenantQueryService.SetProductActiveStateWasCalled);
    }

    [Fact]
    public async Task DeactivateProductAsync_WhenProductIsAlreadyInactive_ReturnsConflict()
    {
        var tenantQueryService = new RecordingTenantQueryService
        {
            CurrentProduct = new ProductDto
            {
                Id = 10,
                Name = "Milk",
                Barcode = "123456789",
                CategoryId = 1,
                UnitPrice = 1.25m,
                CostPrice = 0.75m,
                IsActive = false
            }
        };
        var service = new ProductService(tenantQueryService);

        var result = await service.DeactivateProductAsync(10);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.Conflict, result.FailureType);
        Assert.Equal("Product is already inactive.", result.Message);
        Assert.False(tenantQueryService.SetProductActiveStateWasCalled);
    }

    [Fact]
    public async Task ReactivateProductAsync_WhenProductIsInactive_ReactivatesProduct()
    {
        var tenantQueryService = new RecordingTenantQueryService
        {
            CurrentProduct = new ProductDto
            {
                Id = 10,
                Name = "Milk",
                Barcode = "123456789",
                CategoryId = 1,
                UnitPrice = 1.25m,
                CostPrice = 0.75m,
                IsActive = false
            }
        };
        var service = new ProductService(tenantQueryService);

        var result = await service.ReactivateProductAsync(10);

        Assert.True(result.Succeeded);
        Assert.Equal("Product reactivated.", result.Message);
        Assert.True(result.Data?.IsActive);
        Assert.True(tenantQueryService.SetProductActiveStateWasCalled);
        Assert.True(tenantQueryService.LastActiveState);
    }

    [Fact]
    public async Task ReactivateProductAsync_WhenProductIsAlreadyActive_ReturnsConflict()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new ProductService(tenantQueryService);

        var result = await service.ReactivateProductAsync(10);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.Conflict, result.FailureType);
        Assert.Equal("Product is already active.", result.Message);
        Assert.False(tenantQueryService.SetProductActiveStateWasCalled);
    }

    private sealed class RecordingTenantQueryService : ITenantQueryService
    {
        public bool CreateProductWasCalled { get; private set; }

        public bool UpdateProductWasCalled { get; private set; }

        public bool PatchProductWasCalled { get; private set; }

        public bool SetProductActiveStateWasCalled { get; private set; }

        public bool GetProductsWasCalled { get; private set; }

        public ProductListQuery? LastProductListQuery { get; private set; }

        public bool? LastActiveState { get; private set; }

        public bool ThrowBarcodeConflictOnCreate { get; init; }

        public bool ThrowBarcodeConflictOnUpdate { get; init; }

        public bool ThrowBarcodeConflictOnPatch { get; init; }

        public ProductDto? CurrentProduct { get; set; } = new()
        {
            Id = 10,
            Name = "Milk",
            Barcode = "123456789",
            CategoryId = 1,
            UnitPrice = 1.25m,
            CostPrice = 0.75m,
            IsActive = true
        };

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

        public Task<ProductDto?> GetProductAsync(
            int id,
            bool includeInactive = false,
            CancellationToken cancellationToken = default)
        {
            if (CurrentProduct?.Id != id)
            {
                return Task.FromResult<ProductDto?>(null);
            }

            if (!includeInactive && !CurrentProduct.IsActive)
            {
                return Task.FromResult<ProductDto?>(null);
            }

            return Task.FromResult<ProductDto?>(CurrentProduct);
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

            if (ThrowBarcodeConflictOnCreate)
            {
                throw new ProductBarcodeConflictException();
            }

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

            if (ThrowBarcodeConflictOnUpdate)
            {
                throw new ProductBarcodeConflictException();
            }

            return Task.FromResult<ProductDto?>(new ProductDto
            {
                Id = id,
                Name = request.Name,
                Barcode = request.Barcode,
                CategoryId = request.CategoryId,
                IsActive = CurrentProduct?.IsActive ?? true
            });
        }

        public Task<ProductDto?> PatchProductAsync(
            int id,
            PatchProductRequest request,
            CancellationToken cancellationToken = default)
        {
            PatchProductWasCalled = true;

            if (ThrowBarcodeConflictOnPatch)
            {
                throw new ProductBarcodeConflictException();
            }

            if (CurrentProduct is null || CurrentProduct.Id != id)
            {
                return Task.FromResult<ProductDto?>(null);
            }

            return Task.FromResult<ProductDto?>(new ProductDto
            {
                Id = id,
                Name = request.Name ?? CurrentProduct.Name,
                Barcode = request.Barcode ?? CurrentProduct.Barcode,
                CategoryId = request.CategoryId ?? CurrentProduct.CategoryId,
                UnitPrice = request.UnitPrice ?? CurrentProduct.UnitPrice,
                CostPrice = request.CostPrice ?? CurrentProduct.CostPrice,
                TaxRate = request.TaxRate ?? CurrentProduct.TaxRate,
                MinStockAlert = request.MinStockAlert ?? CurrentProduct.MinStockAlert,
                IsActive = request.IsActive ?? CurrentProduct.IsActive
            });
        }

        public Task<ProductDto?> SetProductActiveStateAsync(
            int id,
            bool isActive,
            CancellationToken cancellationToken = default)
        {
            SetProductActiveStateWasCalled = true;
            LastActiveState = isActive;

            if (CurrentProduct is null || CurrentProduct.Id != id)
            {
                return Task.FromResult<ProductDto?>(null);
            }

            CurrentProduct.IsActive = isActive;
            return Task.FromResult<ProductDto?>(CurrentProduct);
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
