using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Companies.DTOs;

namespace MarketFlow.Application.Features.Companies.Interfaces;

public interface ICompanyService
{
    Task<ServiceResult<IReadOnlyCollection<CompanyDto>>> GetCompaniesAsync(
        CancellationToken cancellationToken = default);
}
