using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Profile.DTOs;
using MarketFlow.Application.Features.Profile.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly IProfileService _profileService;

    public ProfileController(IProfileService profileService)
    {
        _profileService = profileService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ServiceResult<UserProfileResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ServiceResult<UserProfileResponse>>> GetAsync(CancellationToken cancellationToken)
    {
        var result = await _profileService.GetProfileAsync(cancellationToken);
        return result.Succeeded ? Ok(result) : Unauthorized(result);
    }

    [HttpPut]
    [ProducesResponseType(typeof(ServiceResult<UserProfileResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceResult<UserProfileResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ServiceResult<UserProfileResponse>>> UpdateAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var result = await _profileService.UpdateProfileAsync(request, cancellationToken);

        return result.Succeeded ? Ok(result) : BadRequest(result);
    }

    [HttpPut("password")]
    [ProducesResponseType(typeof(ServiceResult<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceResult<bool>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ServiceResult<bool>>> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _profileService.ChangePasswordAsync(request, cancellationToken);

        return result.Succeeded ? Ok(result) : BadRequest(result);
    }
}
