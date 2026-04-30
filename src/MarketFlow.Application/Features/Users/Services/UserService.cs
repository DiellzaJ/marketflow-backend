using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Users.DTOs;
using MarketFlow.Application.Features.Users.Interfaces;

namespace MarketFlow.Application.Features.Users.Services;

public class UserService : IUserService
{
    public Task<ServiceResult<IReadOnlyCollection<UserDto>>> GetUsersAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<UserDto> users = Array.Empty<UserDto>();
        return Task.FromResult(ServiceResult<IReadOnlyCollection<UserDto>>.Success(users));
    }
}
