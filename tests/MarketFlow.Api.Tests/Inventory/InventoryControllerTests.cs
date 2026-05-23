using MarketFlow.Api.Controllers;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Inventory.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Tests.Inventory;

public sealed class InventoryControllerTests
{
    [Fact]
    public async Task GetPosProductsAsync_ReturnsOkWithLookupProducts()
    {
        var product = new PosProductLookupItemDto
        {
            ProductId = 10,
            ProductName = "Milk",
            Barcode = "123",
            UnitPrice = 1.25m,
            AvailableQuantity = 7
        };
        var query = new PosProductLookupQuery
        {
            MarketId = 1,
            Barcode = "123"
        };
        var controller = new InventoryController(new StubInventoryService(
            ServiceResult<IReadOnlyCollection<PosProductLookupItemDto>>.Success([product])));

        var response = await controller.GetPosProductsAsync(query, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var result = Assert.IsType<ServiceResult<IReadOnlyCollection<PosProductLookupItemDto>>>(ok.Value);
        Assert.True(result.Succeeded);
        Assert.Same(product, Assert.Single(result.Data!));
    }

    [Fact]
    public async Task GetPosProductsAsync_WhenLookupIsInvalid_ReturnsBadRequest()
    {
        var result = ServiceResult<IReadOnlyCollection<PosProductLookupItemDto>>.Failure(
            "Barcode or product search is required.");
        var controller = new InventoryController(new StubInventoryService(result));

        var response = await controller.GetPosProductsAsync(
            new PosProductLookupQuery { MarketId = 1 },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(response.Result);
        Assert.Same(result, badRequest.Value);
    }

    private sealed class StubInventoryService : IInventoryService
    {
        private readonly ServiceResult<IReadOnlyCollection<PosProductLookupItemDto>> _posLookupResult;

        public StubInventoryService(
            ServiceResult<IReadOnlyCollection<PosProductLookupItemDto>> posLookupResult)
        {
            _posLookupResult = posLookupResult;
        }

        public Task<ServiceResult<PagedResult<InventoryItemDto>>> GetInventoryAsync(
            InventoryListQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ServiceResult<IReadOnlyCollection<InventoryItemDto>>> GetLowStockInventoryAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ServiceResult<IReadOnlyCollection<PosProductLookupItemDto>>> GetPosProductsAsync(
            PosProductLookupQuery query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_posLookupResult);
        }

        public Task<ServiceResult<InventoryItemDto>> GetInventoryItemAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ServiceResult<PagedResult<InventoryMovementDto>>> GetInventoryMovementsAsync(
            InventoryMovementListQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ServiceResult<PagedResult<InventoryMovementDto>>> GetInventoryMovementsForInventoryAsync(
            int inventoryId,
            InventoryMovementListQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ServiceResult<InventoryItemDto>> CreateInventoryItemAsync(
            CreateInventoryItemRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ServiceResult<InventoryItemDto>> UpdateInventoryItemAsync(
            int id,
            UpdateInventoryItemRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ServiceResult<InventoryItemDto>> PatchInventoryItemAsync(
            int id,
            PatchInventoryItemRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ServiceResult<InventoryItemDto>> AdjustInventoryItemAsync(
            int id,
            AdjustInventoryRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ServiceResult<bool>> TransferInventoryAsync(
            TransferInventoryRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ServiceResult<bool>> DeleteInventoryItemAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
