namespace MarketFlow.Application.Features.Departments.Exceptions;

public sealed class DepartmentNameConflictException : Exception
{
    public DepartmentNameConflictException()
        : base("Department name is already used by another department in this market.")
    {
    }
}
