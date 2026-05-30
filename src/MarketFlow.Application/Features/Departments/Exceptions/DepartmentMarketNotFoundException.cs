namespace MarketFlow.Application.Features.Departments.Exceptions;

public sealed class DepartmentMarketNotFoundException : Exception
{
    public DepartmentMarketNotFoundException()
        : base("Department market was not found.")
    {
    }
}
