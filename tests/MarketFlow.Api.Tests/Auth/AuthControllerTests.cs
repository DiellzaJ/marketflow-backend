using System.Security.Claims;
using System.Text.Json;
using MarketFlow.Api.Controllers;
using MarketFlow.Application.Features.Auth.DTOs;
using MarketFlow.Application.Features.Auth.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Tests.Auth;

public sealed class AuthControllerTests
{
    [Fact]
    public void Me_ReturnsAssignmentFromJwtClaims()
    {
        var controller = new AuthController(new FakeAuthService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "12"),
                        new Claim(ClaimTypes.Email, "seller@marketflow.test"),
                        new Claim(ClaimTypes.Name, "Seller User"),
                        new Claim(ClaimTypes.Role, "Seller"),
                        new Claim("company_id", "3"),
                        new Claim("schema_name", "tenant_3"),
                        new Claim("assigned_market_id", "1"),
                        new Claim("assigned_department_id", "1")
                    ], "Test"))
                }
            }
        };

        var result = Assert.IsType<OkObjectResult>(controller.Me());
        var json = JsonSerializer.Serialize(
            result.Value,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var assignment = root.GetProperty("assignment");

        Assert.Equal("12", root.GetProperty("userId").GetString());
        Assert.Equal("Seller", root.GetProperty("role").GetString());
        Assert.Equal("3", root.GetProperty("companyId").GetString());
        Assert.Equal(1, assignment.GetProperty("marketId").GetInt32());
        Assert.Equal(1, assignment.GetProperty("departmentId").GetInt32());
    }

    private sealed class FakeAuthService : IAuthService
    {
        public Task<AuthResponse> RegisterAsync(RegisterRequest request)
        {
            throw new NotSupportedException();
        }

        public Task<CreateRootAdminResponse> CreateRootAdminAsync(CreateRootAdminRequest request)
        {
            throw new NotSupportedException();
        }

        public Task<AuthResponse> LoginAsync(LoginRequest request)
        {
            throw new NotSupportedException();
        }

        public Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request)
        {
            throw new NotSupportedException();
        }

        public Task LogoutAsync(int userId, string accessTokenId, DateTimeOffset accessTokenExpiresAt)
        {
            throw new NotSupportedException();
        }
    }
}
