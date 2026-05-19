using MarketFlow.Application.Features.Companies.DTOs;

namespace MarketFlow.Application.Common.Interfaces;

public interface ICompanyStore
{
    Task<IReadOnlyCollection<CompanyDto>> GetCompaniesAsync(
        CancellationToken cancellationToken = default);

    Task<CompanyDto?> GetCompanyByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<bool> SchemaNameExistsAsync(
        string schemaName,
        CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<CompanyOnboardingDto?> CreateCompanyAsync(
        CreateCompanyRequest request,
        string schemaName,
        CancellationToken cancellationToken = default);
}
