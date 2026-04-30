using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Users.DTOs;

namespace MarketFlow.Application.Features.Users.Interfaces;

public interface IUserService
{
    Task<ServiceResult<IReadOnlyCollection<UserDto>>> GetUsersAsync(
        CancellationToken cancellationToken = default);
}
