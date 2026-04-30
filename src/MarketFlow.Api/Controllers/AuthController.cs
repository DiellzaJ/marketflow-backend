using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Auth.DTOs;
using MarketFlow.Application.Features.Auth.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<ServiceResult<AuthResponseDto>>> LoginAsync(
        [FromBody] LoginRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, cancellationToken);
        return Ok(result);
    }
}
