using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Users.DTOs;

namespace MarketFlow.Application.Features.Users.Interfaces;

public interface IUserService
{
    Task<ServiceResult<IReadOnlyCollection<UserDto>>> GetUsersAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<UserDto>> GetUserAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<UserDto>> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<UserDto>> UpdateUserAsync(
        int id,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<UserDto>> PatchUserAsync(
        int id,
        PatchUserRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> DeleteUserAsync(
        int id,
        CancellationToken cancellationToken = default);
}
