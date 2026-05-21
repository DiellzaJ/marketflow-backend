namespace MarketFlow.Application.Common.Models;

public class PagedResult<T>
{
    public IReadOnlyCollection<T> Items { get; init; } = Array.Empty<T>();

    public int Page { get; init; }

    public int PageSize { get; init; }

    public int TotalCount { get; init; }

    public int TotalPages { get; init; }

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;
}
