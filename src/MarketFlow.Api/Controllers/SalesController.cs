using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Sales.DTOs;
using MarketFlow.Application.Features.Sales.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SalesController(ISalesService salesService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<SaleDto>>>> GetAsync(
        CancellationToken cancellationToken)
    {
        var result = await salesService.GetSalesAsync(cancellationToken);
        return Ok(result);
    }
}
