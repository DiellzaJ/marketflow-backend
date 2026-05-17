namespace MarketFlow.Application.Common.Interfaces;

public interface ICurrentUserService
{
    int? UserId { get; }

    int? CompanyId { get; }

    string? Email { get; }

    string? Role { get; }

    string? SchemaName { get; }
}
