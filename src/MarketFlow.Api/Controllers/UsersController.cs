using MarketFlow.Api.Authorization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Users.DTOs;
using MarketFlow.Application.Features.Users.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController(IUserService userService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.ReadUsers)]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<UserDto>>>> GetAsync(
        CancellationToken cancellationToken)
    {
        var result = await userService.GetUsersAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.CreateUsers)]
    public async Task<ActionResult<ServiceResult<UserDto>>> CreateAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var result = await userService.CreateUserAsync(request, cancellationToken);

        return result.Succeeded ? CreatedAtAction(nameof(GetAsync), result) : BadRequest(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UpdateUsers)]
    public async Task<ActionResult<ServiceResult<UserDto>>> UpdateAsync(
        int id,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var result = await userService.UpdateUserAsync(id, request, cancellationToken);

        return result.Succeeded ? Ok(result) : NotFound(result);
    }

    [HttpPatch("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UpdateUsers)]
    public async Task<ActionResult<ServiceResult<UserDto>>> PatchAsync(
        int id,
        PatchUserRequest request,
        CancellationToken cancellationToken)
    {
        var result = await userService.PatchUserAsync(id, request, cancellationToken);

        return result.Succeeded ? Ok(result) : NotFound(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.DeleteUsers)]
    public async Task<ActionResult<ServiceResult<bool>>> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await userService.DeleteUserAsync(id, cancellationToken);

        return result.Succeeded ? Ok(result) : NotFound(result);
    }
}
