using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MarketFlow.Api.Authorization;
using MarketFlow.Application.Features.Auth.DTOs;
using MarketFlow.Application.Features.Auth.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var response = await _authService.RegisterAsync(request);

        return Ok(response);
    }

    [Authorize(Policy = AuthorizationPolicies.RootAdminOnly)]
    [HttpPost("root-admins")]
    public async Task<IActionResult> CreateRootAdmin(CreateRootAdminRequest request)
    {
        var response = await _authService.CreateRootAdminAsync(request);

        return Ok(response);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var response = await _authService.LoginAsync(request);

        return Ok(response);
    }

    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken(RefreshTokenRequest request)
    {
        var response = await _authService.RefreshTokenAsync(request);

        return Ok(response);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var accessTokenId = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
        var expiresAtClaim = User.FindFirstValue(JwtRegisteredClaimNames.Exp);

        if (!int.TryParse(userIdClaim, out var userId) ||
            string.IsNullOrWhiteSpace(accessTokenId) ||
            !TryParseUnixTimeSeconds(expiresAtClaim, out var accessTokenExpiresAt))
        {
            return Unauthorized();
        }

        await _authService.LogoutAsync(userId, accessTokenId, accessTokenExpiresAt);

        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var assignedMarketId = GetIntClaim("assigned_market_id");

        return Ok(new
        {
            UserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            Email = User.FindFirstValue(ClaimTypes.Email),
            FullName = User.FindFirstValue(ClaimTypes.Name),
            Role = User.FindFirstValue(ClaimTypes.Role),
            CompanyId = User.FindFirstValue("company_id"),
            SchemaName = User.FindFirstValue("schema_name"),
            Assignment = assignedMarketId.HasValue
                ? new AuthUserAssignmentDto
                {
                    MarketId = assignedMarketId.Value,
                    DepartmentId = GetIntClaim("assigned_department_id")
                }
                : null
        });
    }

    private int? GetIntClaim(string claimType)
    {
        var claimValue = User.FindFirstValue(claimType);

        return int.TryParse(claimValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static bool TryParseUnixTimeSeconds(string? value, out DateTimeOffset dateTimeOffset)
    {
        dateTimeOffset = default;

        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        dateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(seconds);

        return true;
    }
}
