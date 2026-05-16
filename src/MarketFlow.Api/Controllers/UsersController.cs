using MarketFlow.Api.Authorization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Users.DTOs;
using MarketFlow.Application.Features.Users.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.ManageUsers)]
public class UsersController(IUserService userService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<UserDto>>>> GetAsync(
        CancellationToken cancellationToken)
    {
        var result = await userService.GetUsersAsync(cancellationToken);
        return Ok(result);
    }
}
