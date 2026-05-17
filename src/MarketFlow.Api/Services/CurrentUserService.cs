using System.Security.Claims;
using MarketFlow.Application.Common.Interfaces;

namespace MarketFlow.Api.Services;

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public int? UserId => GetIntClaim(ClaimTypes.NameIdentifier);

    public int? CompanyId => GetIntClaim("company_id");

    public string? Email => User.FindFirstValue(ClaimTypes.Email);

    public string? Role => User.FindFirstValue(ClaimTypes.Role);

    public string? SchemaName => User.FindFirstValue("schema_name");

    private ClaimsPrincipal User =>
        _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());

    private int? GetIntClaim(string claimType)
    {
        var value = User.FindFirstValue(claimType);

        return int.TryParse(value, out var parsedValue)
            ? parsedValue
            : null;
    }
}
