using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Features.Companies.DTOs;
using MarketFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MarketFlow.Infrastructure.Persistence;

public sealed class CompanyStore : ICompanyStore
{
    private const string DefaultSubscriptionPlan = "BASIC";
    private const int DefaultMaxMarkets = 5;
    private const int DefaultMaxUsers = 50;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<CompanyStore> _logger;

    public CompanyStore(
        ApplicationDbContext dbContext,
        ILogger<CompanyStore> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<CompanyDto>> GetCompaniesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Companies
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new CompanyDto
            {
                Id = x.Id,
                Name = x.Name,
                SchemaName = x.SchemaName,
                CompanyType = x.CompanyType,
                SubscriptionPlan = x.SubscriptionPlan,
                MaxMarkets = x.MaxMarkets,
                MaxUsers = x.MaxUsers,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<CompanyDto?> GetCompanyByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Companies
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new CompanyDto
            {
                Id = x.Id,
                Name = x.Name,
                SchemaName = x.SchemaName,
                CompanyType = x.CompanyType,
                SubscriptionPlan = x.SubscriptionPlan,
                MaxMarkets = x.MaxMarkets,
                MaxUsers = x.MaxUsers,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> SchemaNameExistsAsync(
        string schemaName,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Companies
            .AnyAsync(x => x.SchemaName == schemaName, cancellationToken);
    }

    public async Task<bool> EmailExistsAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);

        return await _dbContext.Users
            .AnyAsync(x => x.Email == normalizedEmail, cancellationToken);
    }

    public async Task<CompanyOnboardingDto?> CreateCompanyAsync(
        CreateCompanyRequest request,
        string schemaName,
        CancellationToken cancellationToken = default)
    {
        var normalizedAdminEmail = NormalizeEmail(request.CompanyAdmin.Email);

        // GlobalDataSeeder must seed this role before RootAdmin company onboarding can run.
        var companyAdminRole = await _dbContext.Roles
            .FirstOrDefaultAsync(x => x.Name == "CompanyAdmin", cancellationToken);

        if (companyAdminRole is null)
        {
            _logger.LogError("Company onboarding failed because CompanyAdmin role does not exist.");
            return null;
        }

        var company = new Company
        {
            Name = request.Name,
            SchemaName = schemaName,
            CompanyType = request.CompanyType,
            SubscriptionPlan = DefaultSubscriptionPlan,
            MaxMarkets = DefaultMaxMarkets,
            MaxUsers = DefaultMaxUsers,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var companyAdmin = new User
        {
            FullName = request.CompanyAdmin.FullName,
            Email = normalizedAdminEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.CompanyAdmin.Password),
            Company = company,
            Role = companyAdminRole,
            // RootAdmin-initiated onboarding intentionally activates the first company admin immediately.
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Companies.Add(company);
        _dbContext.Users.Add(companyAdmin);

        try
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            _logger.LogWarning(
                "Company onboarding rejected because schema name {SchemaName} or admin email {Email} already exists.",
                schemaName,
                normalizedAdminEmail);

            return null;
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(
                exception,
                "Database error while onboarding company {CompanyName} with schema {SchemaName}.",
                request.Name,
                schemaName);

            throw;
        }

        _logger.LogInformation(
            "Company created: {CompanyName} with schema {SchemaName}.",
            company.Name,
            company.SchemaName);

        return new CompanyOnboardingDto
        {
            Company = new CompanyDto
            {
                Id = company.Id,
                Name = company.Name,
                SchemaName = company.SchemaName,
                CompanyType = company.CompanyType,
                SubscriptionPlan = company.SubscriptionPlan,
                MaxMarkets = company.MaxMarkets,
                MaxUsers = company.MaxUsers,
                IsActive = company.IsActive,
                CreatedAt = company.CreatedAt
            },
            CompanyAdmin = new CompanyAdminSummaryDto
            {
                Id = companyAdmin.Id,
                FullName = companyAdmin.FullName,
                Email = companyAdmin.Email,
                RoleName = companyAdminRole.Name,
                IsActive = companyAdmin.IsActive
            }
        };
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }
}
