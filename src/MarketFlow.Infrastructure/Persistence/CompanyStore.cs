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

    public async Task<CompanyDto?> CreateCompanyAsync(
        CreateCompanyRequest request,
        string schemaName,
        CancellationToken cancellationToken = default)
    {
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

        _dbContext.Companies.Add(company);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            _logger.LogWarning(
                "Company creation rejected because schema name {SchemaName} already exists.",
                schemaName);

            return null;
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(
                exception,
                "Database error while creating company {CompanyName} with schema {SchemaName}.",
                request.Name,
                schemaName);

            throw;
        }

        _logger.LogInformation(
            "Company created: {CompanyName} with schema {SchemaName}.",
            company.Name,
            company.SchemaName);

        return new CompanyDto
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
        };
    }
}
