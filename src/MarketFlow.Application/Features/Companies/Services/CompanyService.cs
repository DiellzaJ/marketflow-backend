using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Companies.DTOs;
using MarketFlow.Application.Features.Companies.Interfaces;

namespace MarketFlow.Application.Features.Companies.Services;

public class CompanyService : ICompanyService
{
    public Task<ServiceResult<IReadOnlyCollection<CompanyDto>>> GetCompaniesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<CompanyDto> companies = Array.Empty<CompanyDto>();
        return Task.FromResult(ServiceResult<IReadOnlyCollection<CompanyDto>>.Success(companies));
    }
}
