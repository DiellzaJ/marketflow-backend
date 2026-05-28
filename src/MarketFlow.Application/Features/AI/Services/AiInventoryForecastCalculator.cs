using MarketFlow.Application.Features.AI.DTOs;

namespace MarketFlow.Application.Features.AI.Services;

public static class AiInventoryForecastCalculator
{
    public static AiInventoryProductForecastDto Calculate(
        AiInventoryForecastDataDto data,
        int salesHistoryDays,
        int forecastDays)
    {
        var averageDailySales = data.TotalQuantitySold <= 0
            ? 0
            : data.TotalQuantitySold / (decimal)salesHistoryDays;
        var forecastDemand = averageDailySales * forecastDays;
        decimal? daysOfStockRemaining = averageDailySales == 0
            ? null
            : data.CurrentStock / averageDailySales;

        return new AiInventoryProductForecastDto
        {
            ProductId = data.ProductId,
            ProductName = data.ProductName,
            CurrentStock = data.CurrentStock,
            TotalQuantitySold = data.TotalQuantitySold,
            AverageDailySales = averageDailySales,
            ForecastDemand = forecastDemand,
            DaysOfStockRemaining = daysOfStockRemaining
        };
    }
}
