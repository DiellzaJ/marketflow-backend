using BCrypt.Net;
using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Features.Users.DTOs;
using Microsoft.EntityFrameworkCore;

namespace MarketFlow.Infrastructure.Persistence;

public sealed class UserStore : IUserStore
{
    private readonly ApplicationDbContext _dbContext;

    public UserStore(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyCollection<UserDto>> GetUsersAsync(
        int? companyId,
        bool includeAllCompanies,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Users
            .AsNoTracking()
            .Include(x => x.Role)
            .AsQueryable();

        if (!includeAllCompanies)
        {
            query = query.Where(x => x.CompanyId == companyId);
        }

        return await query
            .OrderBy(x => x.FullName)
            .Select(x => new UserDto
            {
                Id = x.Id,
                FullName = x.FullName,
                Email = x.Email,
                RoleName = x.Role.Name,
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<UserDto?> CreateUserAsync(
        int companyId,
        CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = NormalizeEmail(request.Email);

        var emailExists = await _dbContext.Users.AnyAsync(x => x.Email == email, cancellationToken);

        if (emailExists)
        {
            return null;
        }

        var companyExists = await _dbContext.Companies
            .AnyAsync(x => x.Id == companyId && x.IsActive, cancellationToken);

        if (!companyExists)
        {
            return null;
        }

        var role = await _dbContext.Roles
            .FirstOrDefaultAsync(x => x.Name == request.RoleName, cancellationToken);

        if (role is null)
        {
            return null;
        }

        var user = new Domain.Entities.User
        {
            FullName = request.FullName.Trim(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            CompanyId = companyId,
            RoleId = role.Id,
            IsActive = request.IsActive
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapUser(user, role.Name);
    }

    public async Task<UserDto?> UpdateUserAsync(
        int id,
        int? companyId,
        bool includeAllCompanies,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await FindEditableUserAsync(id, companyId, includeAllCompanies, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var email = NormalizeEmail(request.Email);
        var emailExists = await _dbContext.Users
            .AnyAsync(x => x.Id != id && x.Email == email, cancellationToken);

        if (emailExists)
        {
            return null;
        }

        var role = await _dbContext.Roles
            .FirstOrDefaultAsync(x => x.Name == request.RoleName, cancellationToken);

        if (role is null)
        {
            return null;
        }

        user.FullName = request.FullName.Trim();
        user.Email = email;
        user.RoleId = role.Id;
        user.IsActive = request.IsActive;

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapUser(user, role.Name);
    }

    public async Task<UserDto?> PatchUserAsync(
        int id,
        int? companyId,
        bool includeAllCompanies,
        PatchUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await FindEditableUserAsync(id, companyId, includeAllCompanies, cancellationToken);

        if (user is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var email = NormalizeEmail(request.Email);
            var emailExists = await _dbContext.Users
                .AnyAsync(x => x.Id != id && x.Email == email, cancellationToken);

            if (emailExists)
            {
                return null;
            }

            user.Email = email;
        }

        if (!string.IsNullOrWhiteSpace(request.FullName))
        {
            user.FullName = request.FullName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.RoleName))
        {
            var role = await _dbContext.Roles
                .FirstOrDefaultAsync(x => x.Name == request.RoleName, cancellationToken);

            if (role is null)
            {
                return null;
            }

            user.RoleId = role.Id;
            user.Role = role;
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        }

        if (request.IsActive.HasValue)
        {
            user.IsActive = request.IsActive.Value;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapUser(user, user.Role.Name);
    }

    public async Task<bool> DeleteUserAsync(
        int id,
        int? companyId,
        bool includeAllCompanies,
        CancellationToken cancellationToken = default)
    {
        var user = await FindEditableUserAsync(id, companyId, includeAllCompanies, cancellationToken);

        if (user is null)
        {
            return false;
        }

        user.IsActive = false;
        user.RefreshTokenHash = null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private async Task<Domain.Entities.User?> FindEditableUserAsync(
        int id,
        int? companyId,
        bool includeAllCompanies,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Users
            .Include(x => x.Role)
            .AsQueryable();

        if (!includeAllCompanies)
        {
            query = query.Where(x => x.CompanyId == companyId);
        }

        return await query.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    private static UserDto MapUser(Domain.Entities.User user, string roleName)
    {
        return new UserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            RoleName = roleName,
            IsActive = user.IsActive
        };
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }
}
