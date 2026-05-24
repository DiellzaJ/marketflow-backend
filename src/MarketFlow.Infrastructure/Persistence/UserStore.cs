using BCrypt.Net;
using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Features.Users.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using RoleAssignmentRules = MarketFlow.Application.Features.Users.Configuration.RoleAssignmentRules;

namespace MarketFlow.Infrastructure.Persistence;

public sealed class UserStore : IUserStore
{
    private const int StaffAssignmentLookupBatchSize = 1_000;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<UserStore> _logger;

    public UserStore(
        ApplicationDbContext dbContext,
        ILogger<UserStore> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
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
                    IsActive = x.IsActive,
                    CompanyId = x.CompanyId,
                    CompanyName = x.Company.Name,
                    CreatedAt = x.CreatedAt
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

    public async Task<UserDto?> GetUserAsync(
        int id,
        int? companyId,
        bool includeAllCompanies,
        CancellationToken cancellationToken = default)
    {
        var user = await FindReadableUserAsync(id, companyId, includeAllCompanies, cancellationToken);

        return user is null
            ? null
            : await MapUserWithAssignmentAsync(user, user.Role.Name, cancellationToken);
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
                $"""
                SELECT EXISTS (
                    SELECT 1
                    FROM {quotedSchemaName}.markets
                    WHERE id = @market_id
                      AND is_active = TRUE
                ) AS "Value"
                """,
                new NpgsqlParameter("market_id", marketId))
            .FirstOrDefaultAsync(cancellationToken);
#pragma warning restore EF1002
    }

    public async Task<bool> CompanyExistsAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Companies
            .AnyAsync(x => x.Id == companyId && x.IsActive, cancellationToken);
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
                    FROM {quotedSchemaName}.departments d
                    INNER JOIN {quotedSchemaName}.markets m
                        ON m.id = d.market_id
                    WHERE d.id = @department_id
                      AND d.market_id = @market_id
                      AND d.is_active = TRUE
                      AND m.is_active = TRUE
                ) AS "Value"
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

        var role = await _dbContext.Roles
            .FirstOrDefaultAsync(x => x.Name == request.RoleName, cancellationToken);

        if (role is null)
        {
            return null;
        }

        user.FullName = request.FullName.Trim();
        user.RoleId = role.Id;
        user.IsActive = request.IsActive;
        user.Role = role;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await UpsertStaffAssignmentAsync(
                user.Company.SchemaName,
                user.Id,
                request.MarketId,
                request.DepartmentId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

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

        if (request.IsActive.HasValue)
        {
            user.IsActive = request.IsActive.Value;
        }

        var currentAssignment = await GetStaffAssignmentAsync(
            user.Company.SchemaName,
            user.Id,
            cancellationToken);
        var updatedMarketId = ResolvePatchedMarketId(user, request, currentAssignment);
        var updatedDepartmentId = ResolvePatchedDepartmentId(
            user,
            request,
            currentAssignment,
            updatedMarketId);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (ShouldUpdateAssignment(user.Role.Name, request))
            {
                await UpsertStaffAssignmentAsync(
                    user.Company.SchemaName,
                    user.Id,
                    updatedMarketId,
                    updatedDepartmentId,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

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
        return await FindUserAsync(
            id,
            companyId,
            includeAllCompanies,
            asNoTracking: false,
            cancellationToken);
    }

    private async Task<Domain.Entities.User?> FindReadableUserAsync(
        int id,
        int? companyId,
        bool includeAllCompanies,
        CancellationToken cancellationToken)
    {
        return await FindUserAsync(
            id,
            companyId,
            includeAllCompanies,
            asNoTracking: true,
            cancellationToken);
    }

    private async Task<Domain.Entities.User?> FindUserAsync(
        int id,
        int? companyId,
        bool includeAllCompanies,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        IQueryable<Domain.Entities.User> query = _dbContext.Users;

        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        query = query
            .Include(x => x.Role)
            .Include(x => x.Company);

        if (!includeAllCompanies)
        {
            query = query.Where(x => x.CompanyId == companyId);
            query = query.Where(x => x.Role.Name != RoleAssignmentRules.RootAdmin);
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
            IsActive = user.IsActive,
            CompanyId = user.CompanyId,
            CompanyName = user.Company.Name,
            CreatedAt = user.CreatedAt
        };
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private static bool ShouldUpdateAssignment(string roleName, PatchUserRequest request)
    {
        return request.MarketId.HasValue ||
               request.DepartmentId.HasValue ||
               string.Equals(roleName, RoleAssignmentRules.CompanyAdmin, StringComparison.OrdinalIgnoreCase);
    }

    private static int? ResolvePatchedMarketId(
        Domain.Entities.User user,
        PatchUserRequest request,
        UserAssignmentSummaryDto? currentAssignment)
    {
        if (string.Equals(user.Role.Name, RoleAssignmentRules.CompanyAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (request.MarketId.HasValue)
        {
            return request.MarketId.Value;
        }

        return currentAssignment?.MarketId;
    }

    private static int? ResolvePatchedDepartmentId(
        Domain.Entities.User user,
        PatchUserRequest request,
        UserAssignmentSummaryDto? currentAssignment,
        int? updatedMarketId)
    {
        if (string.Equals(user.Role.Name, RoleAssignmentRules.CompanyAdmin, StringComparison.OrdinalIgnoreCase) ||
            !updatedMarketId.HasValue)
        {
            return null;
        }

        if (request.MarketId.HasValue)
        {
            return request.DepartmentId;
        }

        if (request.DepartmentId.HasValue)
        {
            return request.DepartmentId.Value;
        }

        return currentAssignment is not null &&
               currentAssignment.MarketId == updatedMarketId.Value
            ? currentAssignment.DepartmentId
            : null;
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

    private async Task UpsertStaffAssignmentAsync(
        string schemaName,
        int userId,
        int? marketId,
        int? departmentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(schemaName))
        {
            return;
        }

        var currentAssignments = await GetStaffAssignmentsAsync(schemaName, [userId], cancellationToken);

        if (currentAssignments.TryGetValue(userId, out var currentAssignment) &&
            currentAssignment.MarketId == marketId &&
            currentAssignment.DepartmentId == departmentId)
        {
            return;
        }

        await DeactivateStaffAssignmentsAsync(schemaName, userId, cancellationToken);

        if (marketId.HasValue)
        {
            await CreateStaffAssignmentAsync(
                schemaName,
                userId,
                marketId.Value,
                departmentId,
                cancellationToken);
        }
    }

    private async Task<UserAssignmentSummaryDto?> GetStaffAssignmentAsync(
        string schemaName,
        int userId,
        CancellationToken cancellationToken)
    {
        var assignments = await GetStaffAssignmentsAsync(schemaName, [userId], cancellationToken);

        return assignments.TryGetValue(userId, out var assignment)
            ? assignment
            : null;
    }

    private async Task DeactivateStaffAssignmentsAsync(
        string schemaName,
        int userId,
        CancellationToken cancellationToken)
    {
        var quotedSchemaName = QuoteIdentifier(schemaName);

#pragma warning disable EF1002
        await _dbContext.Database.ExecuteSqlRawAsync(
            $"""
            UPDATE {quotedSchemaName}.staff_assignments
            SET is_active = FALSE
            WHERE user_id = @user_id
              AND is_active = TRUE;
            """,
            [
                new NpgsqlParameter("user_id", userId)
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
        catch (PostgresException exception) when (IsRecoverableAssignmentLookupException(exception))
        {
            _logger.LogWarning(
                exception,
                "Unable to load staff assignments from tenant schema {SchemaName}. Returning users without assignment summaries.",
                schemaName);

            return [];
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

    private static bool IsRecoverableAssignmentLookupException(PostgresException exception)
    {
        return exception.SqlState is
            "3F000" or // undefined_schema
            "42P01" or // undefined_table
            "42501"; // insufficient_privilege
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private sealed record UserListRow(string SchemaName, UserDto User);

}
