using System.Security.Claims;
using System.Text.Json;
using MarketFlow.Api.Authorization;
using MarketFlow.Api.Controllers;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Profile.DTOs;
using MarketFlow.Application.Features.Profile.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Tests.Profile;

public sealed class ProfileControllerTests
{
    [Fact]
    public async Task GetAsync_ReturnsProfile_WhenAuthenticated()
    {
        var fakeService = new FakeProfileService();
        fakeService.GetResult = ServiceResult<UserProfileResponse>.Success(new UserProfileResponse { Id = 1, FullName = "Me" });

        var controller = new ProfileController(fakeService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([ new Claim(ClaimTypes.NameIdentifier, "1") ], "Test"))
                }
            }
        };

        var result = await controller.GetAsync(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var resultValue = Assert.IsType<ServiceResult<UserProfileResponse>>(ok.Value);
        Assert.True(resultValue.Succeeded);
    }

    [Fact]
    public void Controller_HasAuthorizeAttribute()
    {
        var attr = Assert.Single(
            typeof(ProfileController).GetCustomAttributes(
                typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute),
                inherit: true));

        var authorizeAttribute = Assert.IsType<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>(attr);
        Assert.Equal(AuthorizationPolicies.ActiveUser, authorizeAttribute.Policy);
    }

    [Fact]
    public async Task GetAsync_ReturnsUnauthorized_WhenServiceReturnsFailure()
    {
        var fakeService = new FakeProfileService();
        fakeService.GetResult = ServiceResult<UserProfileResponse>.Failure("not auth");

        var controller = new ProfileController(fakeService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.GetAsync(CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var value = Assert.IsType<ServiceResult<UserProfileResponse>>(unauthorized.Value);
        Assert.False(value.Succeeded);
    }

    private sealed class FakeProfileService : IProfileService
    {
        public ServiceResult<UserProfileResponse> GetResult { get; set; } = ServiceResult<UserProfileResponse>.Failure("no");

        public Task<ServiceResult<UserProfileResponse>> GetProfileAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(GetResult);

        public Task<ServiceResult<UserProfileResponse>> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult<UserProfileResponse>.Success(new UserProfileResponse { Id = 1, FullName = request.FullName }));

        public Task<ServiceResult<bool>> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult<bool>.Success(true));
    }
}
