using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Features.Companies.DTOs;
using MarketFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketFlow.Infrastructure.Persistence;

public sealed class CompanyStore : ICompanyStore
{
    private readonly ApplicationDbContext _dbContext;

    public CompanyStore(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);
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
            IsActive = true
        };

        _dbContext.Companies.Add(company);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return null;
        }

        return new CompanyDto
        {
            Id = company.Id,
            Name = company.Name,
            SchemaName = company.SchemaName,
            CompanyType = company.CompanyType,
            IsActive = company.IsActive
        };
    }
}
