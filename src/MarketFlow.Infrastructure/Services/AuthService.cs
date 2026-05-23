using BCrypt.Net;
using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Features.Auth.DTOs;
using MarketFlow.Application.Features.Auth.Interfaces;
using MarketFlow.Domain.Entities;
using MarketFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MarketFlow.Infrastructure.Services.Auth;

public class AuthService : IAuthService
{
    private const string PlatformAdminSchemaName = "platform_admin";
    private const string RootAdminRoleName = "RootAdmin";

    private readonly ApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthService(
        ApplicationDbContext dbContext,
        ICurrentUserService currentUserService,
        IJwtTokenService jwtTokenService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var email = NormalizeEmail(request.Email);

        var emailExists = await _dbContext.Users
            .AnyAsync(x => x.Email == email);

        if (emailExists)
        {
            throw new InvalidOperationException("A user with this email already exists.");
        }

        var company = await _dbContext.Companies
            .FirstOrDefaultAsync(x => x.Id == request.CompanyId && x.IsActive);

        if (company is null)
        {
            throw new InvalidOperationException("Company does not exist or is inactive.");
        }

        var role = await _dbContext.Roles
            .FirstOrDefaultAsync(x => x.Name == request.RoleName);

        if (role is null)
        {
            throw new InvalidOperationException("Role does not exist.");
        }

        if (role.Name != "Seller")
        {
            throw new InvalidOperationException("Public registration can only create Seller users.");
        }

        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            CompanyId = company.Id,
            RoleId = role.Id,
            IsActive = true
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        user.Company = company;
        user.Role = role;

        return await GenerateAuthResponseAsync(user);
    }

    public async Task<CreateRootAdminResponse> CreateRootAdminAsync(CreateRootAdminRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            throw new InvalidOperationException("Full name, email, and password are required.");
        }

        var currentUserId = _currentUserService.UserId;

        if (currentUserId is null)
        {
            throw new UnauthorizedAccessException("Current user is required.");
        }

        var currentUser = await _dbContext.Users
            .Include(x => x.Role)
            .Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.Id == currentUserId.Value);

        if (currentUser is null ||
            !currentUser.IsActive ||
            !currentUser.Company.IsActive ||
            !string.Equals(currentUser.Role.Name, RootAdminRoleName, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Only active RootAdmin users can create platform administrators.");
        }

        var email = NormalizeEmail(request.Email);

        var emailExists = await _dbContext.Users
            .AnyAsync(x => x.Email == email);

        if (emailExists)
        {
            throw new InvalidOperationException("A user with this email already exists.");
        }

        var rootRole = await _dbContext.Roles
            .FirstOrDefaultAsync(x => x.Name == RootAdminRoleName);

        if (rootRole is null)
        {
            throw new InvalidOperationException("RootAdmin role does not exist.");
        }

        var platformCompany = await _dbContext.Companies
            .FirstOrDefaultAsync(x => x.SchemaName == PlatformAdminSchemaName && x.IsActive);

        if (platformCompany is null)
        {
            throw new InvalidOperationException("Platform admin company does not exist or is inactive.");
        }

        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            CompanyId = platformCompany.Id,
            RoleId = rootRole.Id,
            IsActive = true
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        user.Company = platformCompany;
        user.Role = rootRole;

        return new CreateRootAdminResponse
        {
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = rootRole.Name,
            CompanyId = user.CompanyId,
            SchemaName = platformCompany.SchemaName
        };
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var email = NormalizeEmail(request.Email);

        var user = await _dbContext.Users
            .Include(x => x.Role)
            .Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.Email == email);

        if (user is null)
        {
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        if (!user.IsActive || !user.Company.IsActive)
        {
            throw new UnauthorizedAccessException("User or company is inactive.");
        }

        var passwordIsValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);

        if (!passwordIsValid)
        {
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        user.LastLogin = DateTimeOffset.UtcNow;

        return await GenerateAuthResponseAsync(user);
    }

    public async Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request)
    {
        var users = await _dbContext.Users
            .Include(x => x.Role)
            .Include(x => x.Company)
            .Where(x => x.RefreshTokenHash != null && x.IsActive)
            .ToListAsync();

        var user = users.FirstOrDefault(x =>
            BCrypt.Net.BCrypt.Verify(request.RefreshToken, x.RefreshTokenHash));

        if (user is null)
        {
            throw new UnauthorizedAccessException("Invalid refresh token.");
        }

        return await GenerateAuthResponseAsync(user);
    }

    public async Task LogoutAsync(int userId, string accessTokenId, DateTimeOffset accessTokenExpiresAt)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(x => x.Id == userId);

        if (user is null)
        {
            return;
        }

        user.RefreshTokenHash = null;

        var tokenIsAlreadyRevoked = await _dbContext.RevokedAccessTokens
            .AnyAsync(x => x.TokenId == accessTokenId);

        if (!tokenIsAlreadyRevoked)
        {
            _dbContext.RevokedAccessTokens.Add(new RevokedAccessToken
            {
                TokenId = accessTokenId,
                UserId = userId,
                ExpiresAt = accessTokenExpiresAt,
                RevokedAt = DateTimeOffset.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync();
    }

    private async Task<AuthResponse> GenerateAuthResponseAsync(User user)
    {
        var assignment = await GetActiveStaffAssignmentAsync(user);
        var accessToken = _jwtTokenService.GenerateAccessToken(user, assignment);
        var refreshToken = _jwtTokenService.GenerateRefreshToken();

        user.RefreshTokenHash = _jwtTokenService.HashRefreshToken(refreshToken);

        await _dbContext.SaveChangesAsync();

        return new AuthResponse
        {
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role.Name,
            CompanyId = user.CompanyId,
            SchemaName = user.Company.SchemaName,
            Assignment = assignment,
            AccessToken = accessToken,
            RefreshToken = refreshToken
        };
    }

    private async Task<AuthUserAssignmentDto?> GetActiveStaffAssignmentAsync(User user)
    {
        var schemaName = user.Company.SchemaName;

        if (string.IsNullOrWhiteSpace(schemaName))
        {
            return null;
        }

        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != System.Data.ConnectionState.Open;

        if (closeConnection)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            var currentTransaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();

            if (currentTransaction is NpgsqlTransaction npgsqlTransaction)
            {
                command.Transaction = npgsqlTransaction;
            }

            command.CommandText = $"""
                SELECT sa.market_id,
                       sa.department_id
                FROM {QuoteIdentifier(schemaName)}.staff_assignments sa
                WHERE sa.is_active = TRUE
                  AND sa.user_id = @user_id
                ORDER BY sa.assigned_at DESC, sa.id DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("user_id", user.Id);

            await using var reader = await command.ExecuteReaderAsync();

            return await reader.ReadAsync()
                ? new AuthUserAssignmentDto
                {
                    MarketId = reader.GetInt32(0),
                    DepartmentId = reader.IsDBNull(1) ? null : reader.GetInt32(1)
                }
                : null;
        }
        catch (PostgresException exception) when (IsRecoverableAssignmentLookupException(exception))
        {
            return null;
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }

    private static bool IsRecoverableAssignmentLookupException(PostgresException exception)
    {
        return exception.SqlState is
            "3F000" or // undefined_schema
            "42P01" or // undefined_table
            "42501"; // insufficient_privilege
    }
}
