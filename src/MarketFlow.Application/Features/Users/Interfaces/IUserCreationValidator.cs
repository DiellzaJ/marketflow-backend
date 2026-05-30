using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Users.DTOs;

namespace MarketFlow.Application.Features.Users.Interfaces;

public interface IUserCreationValidator
{
    Task<ServiceResult<int>> ValidateAsync(
        CreateUserRequest request,
        bool isRootAdmin,
        string? currentRole,
        int? currentCompanyId,
        CancellationToken cancellationToken = default);
}
