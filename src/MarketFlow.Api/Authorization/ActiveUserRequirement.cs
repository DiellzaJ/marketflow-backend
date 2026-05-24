using System.Security.Claims;
using MarketFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace MarketFlow.Api.Authorization;

public sealed class ActiveUserRequirement : IAuthorizationRequirement;

public sealed class ActiveUserAuthorizationHandler : AuthorizationHandler<ActiveUserRequirement>
{
    private readonly ApplicationDbContext _dbContext;

    public ActiveUserAuthorizationHandler(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveUserRequirement requirement)
    {
        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(userIdValue, out var userId))
        {
            return;
        }

        var userIsActive = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(x => x.Id == userId && x.IsActive && x.Company.IsActive);

        if (userIsActive)
        {
            context.Succeed(requirement);
        }
    }
}
