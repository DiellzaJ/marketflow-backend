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
    private const string GetUserByIdRouteName = "GetUserById";

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.ReadUsers)]
    [ProducesResponseType(typeof(ServiceResult<IReadOnlyCollection<UserDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceResult<IReadOnlyCollection<UserDto>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ServiceResult<IReadOnlyCollection<UserDto>>>> GetAsync(
        CancellationToken cancellationToken)
    {
        var result = await userService.GetUsersAsync(cancellationToken);
        return result.Succeeded ? Ok(result) : UserListFailure(result);
    }

    [HttpGet("{id:int}", Name = GetUserByIdRouteName)]
    [Authorize(Policy = AuthorizationPolicies.ReadUsers)]
    [ProducesResponseType(typeof(ServiceResult<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceResult<UserDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceResult<UserDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ServiceResult<UserDto>>> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await userService.GetUserAsync(id, cancellationToken);

        return result.Succeeded ? Ok(result) : UserFailure(result);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.CreateUsers)]
    [ProducesResponseType(typeof(ServiceResult<UserDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceResult<UserDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ServiceResult<UserDto>>> CreateAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var result = await userService.CreateUserAsync(request, cancellationToken);

        return result.Succeeded
            ? CreatedAtRoute(GetUserByIdRouteName, new { id = result.Data!.Id }, result)
            : BadRequest(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UpdateUsers)]
    [ProducesResponseType(typeof(ServiceResult<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceResult<UserDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceResult<UserDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ServiceResult<UserDto>>> UpdateAsync(
        int id,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var result = await userService.UpdateUserAsync(id, request, cancellationToken);

        return result.Succeeded ? Ok(result) : UserFailure(result);
    }

    [HttpPatch("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.UpdateUsers)]
    [ProducesResponseType(typeof(ServiceResult<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceResult<UserDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceResult<UserDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ServiceResult<UserDto>>> PatchAsync(
        int id,
        PatchUserRequest request,
        CancellationToken cancellationToken)
    {
        var result = await userService.PatchUserAsync(id, request, cancellationToken);

        return result.Succeeded ? Ok(result) : UserFailure(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AuthorizationPolicies.DeleteUsers)]
    [ProducesResponseType(typeof(ServiceResult<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceResult<bool>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceResult<bool>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ServiceResult<bool>>> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await userService.DeleteUserAsync(id, cancellationToken);

        return result.Succeeded ? Ok(result) : UserStateFailure(result);
    }

    private ActionResult<ServiceResult<UserDto>> UserFailure(ServiceResult<UserDto> result)
    {
        return result.FailureType switch
        {
            ServiceResultFailureType.NotFound => NotFound(result),
            ServiceResultFailureType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, result),
            ServiceResultFailureType.Conflict => Conflict(result),
            _ => BadRequest(result)
        };
    }

    private ActionResult<ServiceResult<IReadOnlyCollection<UserDto>>> UserListFailure(
        ServiceResult<IReadOnlyCollection<UserDto>> result)
    {
        return result.FailureType switch
        {
            ServiceResultFailureType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, result),
            ServiceResultFailureType.Conflict => Conflict(result),
            _ => BadRequest(result)
        };
    }

    private ActionResult<ServiceResult<bool>> UserStateFailure(ServiceResult<bool> result)
    {
        return result.FailureType switch
        {
            ServiceResultFailureType.NotFound => NotFound(result),
            ServiceResultFailureType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, result),
            ServiceResultFailureType.Conflict => Conflict(result),
            _ => BadRequest(result)
        };
    }
}
