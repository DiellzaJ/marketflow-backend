using MarketFlow.Api.Controllers;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Sales.DTOs;
using MarketFlow.Application.Features.Sales.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Tests.Sales;

public sealed class SalesControllerTests
{
    [Fact]
    public async Task GetByIdAsync_WhenSaleExists_ReturnsOk()
    {
        var saleDetails = new SaleDetailsResponse
        {
            Id = 1,
            ReferenceNumber = "SALE-001",
            TotalAmount = 25.50m,
            CreatedAt = DateTimeOffset.UtcNow,
            CashierName = "John Doe",
            MarketName = "Main Market",
            Items = [
                new SaleItemResponse
                {
                    ProductId = 5,
                    ProductName = "Coca Cola",
                    Quantity = 2,
                    UnitPrice = 1.50m,
                    LineTotal = 3.00m
                }
            ]
        };

        var controller = new SalesController(new StubSalesService(ServiceResult<SaleDetailsResponse>.Success(saleDetails)));

        var response = await controller.GetByIdAsync(1, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var result = Assert.IsType<ServiceResult<SaleDetailsResponse>>(ok.Value);

        Assert.True(result.Succeeded);
        Assert.Same(saleDetails, result.Data);
    }

    [Fact]
    public async Task GetByIdAsync_WhenSaleDoesNotExist_ReturnsNotFound()
    {
        var expectedResult = ServiceResult<SaleDetailsResponse>.Failure("Sale was not found.");
        var controller = new SalesController(new StubSalesService(expectedResult));

        var response = await controller.GetByIdAsync(10, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(response.Result);
        Assert.Same(expectedResult, notFound.Value);
    }

    private sealed class StubSalesService : ISalesService
    {
        private readonly ServiceResult<SaleDetailsResponse> _saleDetailsResult;

        public StubSalesService(ServiceResult<SaleDetailsResponse> saleDetailsResult)
        {
            _saleDetailsResult = saleDetailsResult;
        }

        public Task<ServiceResult<IReadOnlyCollection<SaleDto>>> GetSalesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceResult<SaleDto>> CreateSaleAsync(CreateSaleRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceResult<SaleDetailsResponse>> GetSaleDetailsAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_saleDetailsResult);

        public Task<ServiceResult<SaleDto>> UpdateSaleAsync(int id, UpdateSaleRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceResult<SaleDto>> PatchSaleAsync(int id, PatchSaleRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceResult<bool>> DeleteSaleAsync(int id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
