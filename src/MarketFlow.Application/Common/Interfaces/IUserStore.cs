using MarketFlow.Application.Features.Users.DTOs;

namespace MarketFlow.Application.Common.Interfaces;

public interface IUserStore
{
    Task<IReadOnlyCollection<UserDto>> GetUsersAsync(
        int? companyId,
        bool includeAllCompanies,
        CancellationToken cancellationToken = default);

    Task<UserDto?> GetUserAsync(
        int id,
        int? companyId,
        bool includeAllCompanies,
        CancellationToken cancellationToken = default);

    Task<UserDto?> CreateUserAsync(
        int companyId,
        CreateUserRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the market exists in the selected company's tenant schema.
    /// </summary>
    Task<bool> MarketExistsAsync(
        int companyId,
        int marketId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the department exists inside the selected market in the company's tenant schema.
    /// </summary>
    Task<bool> DepartmentExistsAsync(
        int companyId,
        int marketId,
        int departmentId,
        CancellationToken cancellationToken = default);

    Task<UserDto?> UpdateUserAsync(
        int id,
        int? companyId,
        bool includeAllCompanies,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default);

    Task<UserDto?> PatchUserAsync(
        int id,
        int? companyId,
        bool includeAllCompanies,
        PatchUserRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteUserAsync(
        int id,
        int? companyId,
        bool includeAllCompanies,
        CancellationToken cancellationToken = default);
}
