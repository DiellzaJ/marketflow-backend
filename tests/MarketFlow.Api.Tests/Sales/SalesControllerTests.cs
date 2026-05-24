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

    [Fact]
    public async Task CreateAsync_WhenSaleIsCreated_ReturnsReferenceNumber()
    {
        var sale = new SaleDto
        {
            Id = 7,
            ReferenceNumber = "SALE-000007",
            MarketId = 2,
            SaleDate = DateOnly.FromDateTime(DateTime.UtcNow),
            PaymentMethod = "Cash",
            TotalAmount = 12.50m
        };
        var controller = new SalesController(new StubSalesService(
            ServiceResult<SaleDetailsResponse>.Failure("unused"),
            ServiceResult<SaleDto>.Success(sale, "Sale created.")));

        var response = await controller.CreateAsync(new CreateSaleRequest(), CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(response.Result);
        var result = Assert.IsType<ServiceResult<SaleDto>>(created.Value);
        Assert.True(result.Succeeded);
        Assert.Equal("SALE-000007", result.Data?.ReferenceNumber);
    }

    [Fact]
    public async Task GetAsync_WhenSalesExist_ReturnsReferenceNumbers()
    {
        var sale = new SaleDto
        {
            Id = 7,
            ReferenceNumber = "SALE-000007",
            MarketId = 2,
            SaleDate = DateOnly.FromDateTime(DateTime.UtcNow),
            PaymentMethod = "Cash",
            TotalAmount = 12.50m
        };
        var controller = new SalesController(new StubSalesService(
            ServiceResult<SaleDetailsResponse>.Failure("unused"),
            salesResult: ServiceResult<IReadOnlyCollection<SaleDto>>.Success([sale])));

        var response = await controller.GetAsync(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var result = Assert.IsType<ServiceResult<IReadOnlyCollection<SaleDto>>>(ok.Value);
        Assert.Equal("SALE-000007", Assert.Single(result.Data!).ReferenceNumber);
    }

    private sealed class StubSalesService : ISalesService
    {
        private readonly ServiceResult<SaleDetailsResponse> _saleDetailsResult;
        private readonly ServiceResult<SaleDto> _createResult;
        private readonly ServiceResult<IReadOnlyCollection<SaleDto>> _salesResult;

        public StubSalesService(
            ServiceResult<SaleDetailsResponse> saleDetailsResult,
            ServiceResult<SaleDto>? createResult = null,
            ServiceResult<IReadOnlyCollection<SaleDto>>? salesResult = null)
        {
            _saleDetailsResult = saleDetailsResult;
            _createResult = createResult ?? ServiceResult<SaleDto>.Failure("unused");
            _salesResult = salesResult ?? ServiceResult<IReadOnlyCollection<SaleDto>>.Success([]);
        }

        public Task<ServiceResult<IReadOnlyCollection<SaleDto>>> GetSalesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_salesResult);

        public Task<ServiceResult<SaleDto>> CreateSaleAsync(CreateSaleRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(_createResult);

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
