using MarketFlow.Api.Controllers;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Users.DTOs;
using MarketFlow.Application.Features.Users.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Tests.Users;

public sealed class UsersControllerTests
{
    [Fact]
    public async Task GetAsync_ReturnsUserAssignmentSummary()
    {
        var users = new List<UserDto>
        {
            new()
            {
                Id = 1,
                FullName = "Store Seller",
                Email = "seller@freshmarket.test",
                RoleName = "Seller",
                IsActive = true,
                Assignment = new UserAssignmentSummaryDto
                {
                    MarketId = 3,
                    MarketName = "Central Market",
                    DepartmentId = 4,
                    DepartmentName = "Produce"
                }
            }
        };
        var controller = new UsersController(new FakeUserService
        {
            GetUsersResult = ServiceResult<IReadOnlyCollection<UserDto>>.Success(users)
        });

        var response = await controller.GetAsync(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var result = Assert.IsType<ServiceResult<IReadOnlyCollection<UserDto>>>(ok.Value);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        var user = Assert.Single(result.Data);
        Assert.Equal(3, user.Assignment?.MarketId);
        Assert.Equal("Central Market", user.Assignment?.MarketName);
        Assert.Equal(4, user.Assignment?.DepartmentId);
        Assert.Equal("Produce", user.Assignment?.DepartmentName);
    }

    [Fact]
    public async Task GetAsync_WhenServiceReturnsForbidden_ReturnsForbidden()
    {
        var controller = new UsersController(new FakeUserService
        {
            GetUsersResult = ServiceResult<IReadOnlyCollection<UserDto>>.Failure(
                "Forbidden user list access.",
                ServiceResultFailureType.Forbidden)
        });

        var response = await controller.GetAsync(CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_WhenUserIsCreated_ReturnsAssignmentSummary()
    {
        var createdUser = new UserDto
        {
            Id = 5,
            FullName = "Store Seller",
            Email = "seller@freshmarket.test",
            RoleName = "Seller",
            IsActive = true,
            CompanyId = 12,
            CompanyName = "Fresh Market",
            CreatedAt = new DateTimeOffset(2026, 5, 3, 14, 30, 0, TimeSpan.Zero),
            Assignment = new UserAssignmentSummaryDto
            {
                MarketId = 3,
                MarketName = "Central Market"
            }
        };
        var controller = new UsersController(new FakeUserService
        {
            CreateUserResult = ServiceResult<UserDto>.Success(createdUser, "User created.")
        });

        var response = await controller.CreateAsync(
            new CreateUserRequest
            {
                FullName = "Store Seller",
                Email = "seller@freshmarket.test",
                Password = "Seller12345",
                RoleName = "Seller",
                MarketId = 3
            },
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtRouteResult>(response.Result);
        var result = Assert.IsType<ServiceResult<UserDto>>(created.Value);
        Assert.True(result.Succeeded);
        Assert.Equal("GetUserById", created.RouteName);
        Assert.Equal(createdUser.Id, created.RouteValues?["id"]);
        Assert.Equal(3, result.Data?.Assignment?.MarketId);
        Assert.Null(result.Data?.Assignment?.DepartmentId);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsUserDetailsForModal()
    {
        var user = new UserDto
        {
            Id = 7,
            FullName = "Store Seller",
            Email = "seller@freshmarket.test",
            RoleName = "Seller",
            IsActive = true,
            CompanyId = 12,
            CompanyName = "Fresh Market",
            CreatedAt = new DateTimeOffset(2026, 5, 3, 14, 30, 0, TimeSpan.Zero),
            Assignment = new UserAssignmentSummaryDto
            {
                MarketId = 3,
                MarketName = "Central Market",
                DepartmentId = 4,
                DepartmentName = "Produce"
            }
        };
        var controller = new UsersController(new FakeUserService
        {
            GetUserResult = ServiceResult<UserDto>.Success(user)
        });

        var response = await controller.GetByIdAsync(7, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var result = Assert.IsType<ServiceResult<UserDto>>(ok.Value);
        Assert.True(result.Succeeded);
        Assert.Equal(12, result.Data?.CompanyId);
        Assert.Equal("Fresh Market", result.Data?.CompanyName);
        Assert.Equal(4, result.Data?.Assignment?.DepartmentId);
    }

    [Fact]
    public async Task GetByIdAsync_WhenServiceReturnsForbidden_ReturnsForbidden()
    {
        var controller = new UsersController(new FakeUserService
        {
            GetUserResult = ServiceResult<UserDto>.Failure(
                "Forbidden user access.",
                ServiceResultFailureType.Forbidden)
        });

        var response = await controller.GetByIdAsync(7, CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_WhenServiceReturnsValidation_ReturnsBadRequest()
    {
        var controller = new UsersController(new FakeUserService
        {
            DeleteUserResult = ServiceResult<bool>.Failure("You cannot deactivate your own account.")
        });

        var response = await controller.DeleteAsync(7, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(response.Result);
        var result = Assert.IsType<ServiceResult<bool>>(badRequest.Value);
        Assert.Equal("You cannot deactivate your own account.", result.Message);
    }

    private sealed class FakeUserService : IUserService
    {
        public ServiceResult<IReadOnlyCollection<UserDto>> GetUsersResult { get; init; } =
            ServiceResult<IReadOnlyCollection<UserDto>>.Success(Array.Empty<UserDto>());

        public ServiceResult<UserDto> CreateUserResult { get; init; } =
            ServiceResult<UserDto>.Failure("Not configured.");

        public ServiceResult<UserDto> GetUserResult { get; init; } =
            ServiceResult<UserDto>.Failure("Not configured.");

        public ServiceResult<bool> DeleteUserResult { get; init; } =
            ServiceResult<bool>.Failure("Not configured.");

        public Task<ServiceResult<IReadOnlyCollection<UserDto>>> GetUsersAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GetUsersResult);
        }

        public Task<ServiceResult<UserDto>> GetUserAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GetUserResult);
        }

        public Task<ServiceResult<UserDto>> CreateUserAsync(
            CreateUserRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateUserResult);
        }

        public Task<ServiceResult<UserDto>> UpdateUserAsync(
            int id,
            UpdateUserRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ServiceResult<UserDto>.Failure("Not configured."));
        }

        public Task<ServiceResult<UserDto>> PatchUserAsync(
            int id,
            PatchUserRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ServiceResult<UserDto>.Failure("Not configured."));
        }

        public Task<ServiceResult<bool>> DeleteUserAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DeleteUserResult);
        }
    }
}
