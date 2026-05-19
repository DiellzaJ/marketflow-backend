using MarketFlow.Application.Features.Companies.DTOs;

namespace MarketFlow.Application.Common.Interfaces;

public interface ICompanyStore
{
    Task<IReadOnlyCollection<CompanyDto>> GetCompaniesAsync(
        CancellationToken cancellationToken = default);

    Task<bool> SchemaNameExistsAsync(
        string schemaName,
        CancellationToken cancellationToken = default);

    Task<CompanyDto?> CreateCompanyAsync(
        CreateCompanyRequest request,
        string schemaName,
        CancellationToken cancellationToken = default);
}
