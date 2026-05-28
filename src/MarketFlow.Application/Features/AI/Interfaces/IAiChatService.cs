using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;

namespace MarketFlow.Application.Features.AI.Interfaces;

public interface IAiChatService
{
    Task<ServiceResult<AiChatResponse>> ChatAsync(
        AiChatRequest request,
        CancellationToken cancellationToken = default);
}
