using MarketFlow.Api.Controllers;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Dashboard.DTOs;
using MarketFlow.Application.Features.Dashboard.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Tests.Dashboard;

public sealed class DashboardControllerTests
{
    [Fact]
    public async Task GetSalesSummaryAsync_WhenQuerySucceeds_ReturnsOk()
    {
        var summary = new SalesSummaryDto
        {
            TotalRevenue = 90,
            TotalSales = 3,
            TotalItemsSold = 8,
            AverageSaleAmount = 30
        };
        var controller = new DashboardController(new StubDashboardService(
            ServiceResult<SalesSummaryDto>.Success(summary)));

        var response = await controller.GetSalesSummaryAsync(new SalesSummaryQuery(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var result = Assert.IsType<ServiceResult<SalesSummaryDto>>(ok.Value);
        Assert.True(result.Succeeded);
        Assert.Same(summary, result.Data);
    }

    [Fact]
    public async Task GetSalesSummaryAsync_WhenQueryFails_ReturnsBadRequest()
    {
        var expectedResult = ServiceResult<SalesSummaryDto>.Failure("From date must be on or before to date.");
        var controller = new DashboardController(new StubDashboardService(expectedResult));

        var response = await controller.GetSalesSummaryAsync(new SalesSummaryQuery(), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(response.Result);
        Assert.Same(expectedResult, badRequest.Value);
    }

    private sealed class StubDashboardService(ServiceResult<SalesSummaryDto> result) : IDashboardService
    {
        public Task<ServiceResult<SalesSummaryDto>> GetSalesSummaryAsync(
            SalesSummaryQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
