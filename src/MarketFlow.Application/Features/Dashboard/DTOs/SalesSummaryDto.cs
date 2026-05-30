namespace MarketFlow.Application.Features.Dashboard.DTOs;

public class SalesSummaryDto
{
    public decimal TotalRevenue { get; set; }

    public long TotalSales { get; set; }

    public long TotalItemsSold { get; set; }

    public decimal AverageSaleAmount { get; set; }
}
