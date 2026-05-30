using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;

namespace MarketFlow.Application.Features.AI.Interfaces;

public interface IAiReportQueryService
{
    Task<ServiceResult<NaturalLanguageReportResponseDto>> QueryAsync(
        NaturalLanguageReportRequest request,
        CancellationToken cancellationToken = default);
}
