using BCrypt.Net;
using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Features.Users.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace MarketFlow.Infrastructure.Persistence;

public sealed class UserStore : IUserStore
{
    private const int StaffAssignmentLookupBatchSize = 1_000;

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
            .Include(x => x.Company)
            .AsQueryable();

        if (!includeAllCompanies)
        {
            query = query.Where(x => x.CompanyId == companyId);
        }

        var users = await query
            .OrderBy(x => x.FullName)
            .Select(x => new UserListRow(
                x.Company.SchemaName,
                new UserDto
                {
                    Id = x.Id,
                    FullName = x.FullName,
                    Email = x.Email,
                    RoleName = x.Role.Name,
                    IsActive = x.IsActive
                }))
            .ToListAsync(cancellationToken);

        foreach (var schemaGroup in users.GroupBy(x => x.SchemaName))
        {
            var assignments = await GetStaffAssignmentsAsync(
                schemaGroup.Key,
                schemaGroup.Select(x => x.User.Id),
                cancellationToken);

            foreach (var row in schemaGroup)
            {
                if (assignments.TryGetValue(row.User.Id, out var assignment))
                {
                    row.User.Assignment = assignment;
                }
            }
        }

        return users.Select(x => x.User).ToList();
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

        var company = await _dbContext.Companies
            .FirstOrDefaultAsync(x => x.Id == companyId && x.IsActive, cancellationToken);

        if (company is null)
        {
            return null;
        }

        var role = await _dbContext.Roles
            // CreateUserAsync receives a role name normalized by UserService.
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

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (request.MarketId.HasValue)
            {
                await CreateStaffAssignmentAsync(
                    company.SchemaName,
                    user.Id,
                    request.MarketId.Value,
                    request.DepartmentId,
                    cancellationToken);
            }

            user.Role = role;
            var userDto = MapUser(user, role.Name);
            var assignments = await GetStaffAssignmentsAsync(
                company.SchemaName,
                [user.Id],
                cancellationToken);

            if (assignments.TryGetValue(user.Id, out var assignment))
            {
                userDto.Assignment = assignment;
            }

            await transaction.CommitAsync(cancellationToken);

            return userDto;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> MarketExistsAsync(
        int companyId,
        int marketId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetCompanySchemaNameAsync(companyId, cancellationToken);

        if (schemaName is null)
        {
            return false;
        }

        var quotedSchemaName = QuoteIdentifier(schemaName);

#pragma warning disable EF1002
        // Tenant schema names are persisted validated identifiers; values remain parameterized.
        return await _dbContext.Database
            .SqlQueryRaw<bool>(
                $"SELECT EXISTS (SELECT 1 FROM {quotedSchemaName}.markets WHERE id = @market_id);",
                new NpgsqlParameter("market_id", marketId))
            .FirstOrDefaultAsync(cancellationToken);
#pragma warning restore EF1002
    }

    public async Task<bool> DepartmentExistsAsync(
        int companyId,
        int marketId,
        int departmentId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetCompanySchemaNameAsync(companyId, cancellationToken);

        if (schemaName is null)
        {
            return false;
        }

        var quotedSchemaName = QuoteIdentifier(schemaName);

#pragma warning disable EF1002
        // Tenant schema names are persisted validated identifiers; values remain parameterized.
        return await _dbContext.Database
            .SqlQueryRaw<bool>(
                $"""
                SELECT EXISTS (
                    SELECT 1
                    FROM {quotedSchemaName}.departments
                    WHERE id = @department_id
                      AND market_id = @market_id
                );
                """,
                new NpgsqlParameter("department_id", departmentId),
                new NpgsqlParameter("market_id", marketId))
            .FirstOrDefaultAsync(cancellationToken);
#pragma warning restore EF1002
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

        return await MapUserWithAssignmentAsync(user, role.Name, cancellationToken);
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

        return await MapUserWithAssignmentAsync(user, user.Role.Name, cancellationToken);
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
            .Include(x => x.Company)
            .AsQueryable();

        if (!includeAllCompanies)
        {
            query = query.Where(x => x.CompanyId == companyId);
        }

        return await query.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    private async Task<UserDto> MapUserWithAssignmentAsync(
        Domain.Entities.User user,
        string roleName,
        CancellationToken cancellationToken)
    {
        var userDto = MapUser(user, roleName);

        if (string.IsNullOrWhiteSpace(user.Company.SchemaName))
        {
            return userDto;
        }

        var assignments = await GetStaffAssignmentsAsync(
            user.Company.SchemaName,
            [user.Id],
            cancellationToken);

        if (assignments.TryGetValue(user.Id, out var assignment))
        {
            userDto.Assignment = assignment;
        }

        return userDto;
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

    private async Task<string?> GetCompanySchemaNameAsync(
        int companyId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Companies
            .Where(x => x.Id == companyId && x.IsActive)
            .Select(x => x.SchemaName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task CreateStaffAssignmentAsync(
        string schemaName,
        int userId,
        int marketId,
        int? departmentId,
        CancellationToken cancellationToken)
    {
        var quotedSchemaName = QuoteIdentifier(schemaName);

#pragma warning disable EF1002
        // Tenant schema names are persisted validated identifiers; values remain parameterized.
        await _dbContext.Database.ExecuteSqlRawAsync(
            $"""
            INSERT INTO {quotedSchemaName}.staff_assignments (
                user_id,
                market_id,
                department_id,
                is_active
            )
            VALUES (
                @user_id,
                @market_id,
                @department_id,
                TRUE
            );
            """,
            [
                new Npgsql.NpgsqlParameter("user_id", userId),
                new Npgsql.NpgsqlParameter("market_id", marketId),
                new Npgsql.NpgsqlParameter("department_id", departmentId ?? (object)DBNull.Value)
            ],
            cancellationToken);
#pragma warning restore EF1002
    }

    private async Task<Dictionary<int, UserAssignmentSummaryDto>> GetStaffAssignmentsAsync(
        string schemaName,
        IEnumerable<int> userIds,
        CancellationToken cancellationToken)
    {
        var userIdArray = userIds.Distinct().ToArray();

        if (userIdArray.Length == 0)
        {
            return [];
        }

        var assignments = new Dictionary<int, UserAssignmentSummaryDto>();
        var quotedSchemaName = QuoteIdentifier(schemaName);
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != System.Data.ConnectionState.Open;

        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var currentTransaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();

            foreach (var userIdBatch in userIdArray.Chunk(StaffAssignmentLookupBatchSize))
            {
                await using var command = connection.CreateCommand();

                if (currentTransaction is NpgsqlTransaction npgsqlTransaction)
                {
                    command.Transaction = npgsqlTransaction;
                }

                command.CommandText = $"""
                    SELECT DISTINCT ON (sa.user_id)
                        sa.user_id,
                        sa.market_id,
                        m.name AS market_name,
                        sa.department_id,
                        d.name AS department_name
                    FROM {quotedSchemaName}.staff_assignments sa
                    INNER JOIN {quotedSchemaName}.markets m ON m.id = sa.market_id
                    LEFT JOIN {quotedSchemaName}.departments d ON d.id = sa.department_id
                        AND d.market_id = sa.market_id
                    WHERE sa.is_active = TRUE
                      AND sa.user_id = ANY (@user_ids)
                    ORDER BY sa.user_id, sa.assigned_at DESC, sa.id DESC;
                    """;
                command.Parameters.Add(new NpgsqlParameter<int[]>("user_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer)
                {
                    TypedValue = userIdBatch
                });

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    var userId = reader.GetInt32(0);
                    assignments[userId] = new UserAssignmentSummaryDto
                    {
                        MarketId = reader.GetInt32(1),
                        MarketName = reader.GetString(2),
                        DepartmentId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                        DepartmentName = reader.IsDBNull(4) ? null : reader.GetString(4)
                    };
                }
            }
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }

        return assignments;
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private sealed record UserListRow(string SchemaName, UserDto User);

}
