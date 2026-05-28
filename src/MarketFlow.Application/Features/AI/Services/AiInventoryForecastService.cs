using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;

namespace MarketFlow.Application.Features.AI.Services;

public class AiInventoryForecastService(IAiInventoryForecastDataService forecastDataService) : IAiInventoryForecastService
{
    public async Task<ServiceResult<AiInventoryForecastResponse>> GenerateInventoryForecastAsync(
        AiInventoryForecastRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.SalesHistoryDays <= 0 || request.ForecastDays <= 0)
        {
            return ServiceResult<AiInventoryForecastResponse>.Failure(
                "Sales history days and forecast days must be positive values.");
        }

        if ((request.MarketId.HasValue && request.MarketId <= 0) ||
            (request.DepartmentId.HasValue && request.DepartmentId <= 0))
        {
            return ServiceResult<AiInventoryForecastResponse>.Failure(
                "Market and department filters must be positive values.");
        }

        var normalizedRequest = new AiInventoryForecastRequest
        {
            SalesHistoryDays = Math.Clamp(request.SalesHistoryDays, 1, 365),
            ForecastDays = Math.Clamp(request.ForecastDays, 1, 365),
            MarketId = request.MarketId,
            DepartmentId = request.DepartmentId
        };

        var forecastData = await forecastDataService.GetAiInventoryForecastDataAsync(
            normalizedRequest,
            cancellationToken);

        var response = new AiInventoryForecastResponse
        {
            SalesHistoryDays = normalizedRequest.SalesHistoryDays,
            ForecastDays = normalizedRequest.ForecastDays,
            MarketId = normalizedRequest.MarketId,
            DepartmentId = normalizedRequest.DepartmentId,
            Products = forecastData
                .Select(data => AiInventoryForecastCalculator.Calculate(
                    data,
                    normalizedRequest.SalesHistoryDays,
                    normalizedRequest.ForecastDays))
                .OrderByDescending(product => product.ForecastDemand)
                .ThenBy(product => product.ProductName)
                .ThenBy(product => product.ProductId)
                .ToArray()
        };

        return ServiceResult<AiInventoryForecastResponse>.Success(response);
    }
}
