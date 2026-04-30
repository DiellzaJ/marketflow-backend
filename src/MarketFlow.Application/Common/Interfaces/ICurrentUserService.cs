namespace MarketFlow.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }

    Guid? CompanyId { get; }

    string? Email { get; }
}
