namespace MarketFlow.Application.Features.Sales.DTOs;

public class SaleHistoryQuery
{
    public DateOnly? DateFrom { get; set; }

    public DateOnly? DateTo { get; set; }

    public int? MarketId { get; set; }

    public int? CashierUserId { get; set; }

    public int? UserId { get; set; }

    public string? Status { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public string? SortBy { get; set; } = "saleDate";

    public string? SortDirection { get; set; } = "desc";
}
