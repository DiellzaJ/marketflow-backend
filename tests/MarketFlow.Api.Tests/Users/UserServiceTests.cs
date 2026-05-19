using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Features.Users.DTOs;
using MarketFlow.Application.Features.Users.Services;

namespace MarketFlow.Api.Tests.Users;

public sealed class UserServiceTests
{
    [Fact]
    public async Task CreateUserAsync_ForCompanyAdmin_DerivesCompanyFromCurrentUser()
    {
        var store = new FakeUserStore();
        var service = new UserService(
            store,
            new FakeCurrentUserService { CompanyId = 12, Role = "CompanyAdmin" });

        var result = await service.CreateUserAsync(new CreateUserRequest
        {
            FullName = "Store Seller",
            Email = "seller@freshmarket.test",
            Password = "Seller12345",
            CompanyId = 99,
            RoleName = "Seller",
            MarketId = 3
        });

        Assert.True(result.Succeeded);
        Assert.Equal(12, store.CreatedCompanyId);
    }

    [Fact]
    public async Task CreateUserAsync_ForRootAdmin_UsesSelectedCompany()
    {
        var store = new FakeUserStore();
        var service = new UserService(
            store,
            new FakeCurrentUserService { CompanyId = 1, Role = "RootAdmin" });

        var result = await service.CreateUserAsync(new CreateUserRequest
        {
            FullName = "Store Seller",
            Email = "seller@freshmarket.test",
            Password = "Seller12345",
            CompanyId = 25,
            RoleName = "Seller",
            MarketId = 3
        });

        Assert.True(result.Succeeded);
        Assert.Equal(25, store.CreatedCompanyId);
    }

    [Fact]
    public async Task CreateUserAsync_ForRootAdminWithoutSelectedCompany_ReturnsValidationError()
    {
        var service = new UserService(
            new FakeUserStore(),
            new FakeCurrentUserService { CompanyId = 1, Role = "RootAdmin" });

        var result = await service.CreateUserAsync(new CreateUserRequest
        {
            FullName = "Store Seller",
            Email = "seller@freshmarket.test",
            Password = "Seller12345",
            RoleName = "Seller",
            MarketId = 3
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Company is required when RootAdmin creates a user.", result.Message);
    }

    [Theory]
    [InlineData("Seller")]
    [InlineData("MainOperator")]
    public async Task CreateUserAsync_ForMarketRolesWithoutMarket_ReturnsValidationError(string roleName)
    {
        var service = CreateCompanyAdminService();

        var result = await service.CreateUserAsync(ValidRequest(roleName));

        Assert.False(result.Succeeded);
        Assert.Equal($"{roleName} requires market assignment.", result.Message);
    }

    [Theory]
    [InlineData("Seller")]
    [InlineData("MainOperator")]
    public async Task CreateUserAsync_ForMarketRolesWithDepartment_ReturnsValidationError(string roleName)
    {
        var service = CreateCompanyAdminService();
        var request = ValidRequest(roleName);
        request.MarketId = 3;
        request.DepartmentId = 4;

        var result = await service.CreateUserAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal($"{roleName} cannot be assigned to a department.", result.Message);
    }

    [Fact]
    public async Task CreateUserAsync_ForDepartmentManagerWithoutMarketAndDepartment_ReturnsValidationError()
    {
        var service = CreateCompanyAdminService();

        var result = await service.CreateUserAsync(ValidRequest("DepartmentManager"));

        Assert.False(result.Succeeded);
        Assert.Equal("DepartmentManager requires market and department assignment.", result.Message);
    }

    [Fact]
    public async Task CreateUserAsync_ForCompanyAdminWithNoAssignment_CreatesUser()
    {
        var store = new FakeUserStore();
        var service = new UserService(
            store,
            new FakeCurrentUserService { CompanyId = 12, Role = "CompanyAdmin" });

        var result = await service.CreateUserAsync(ValidRequest("CompanyAdmin"));

        Assert.True(result.Succeeded);
        Assert.Null(store.CreatedRequest?.MarketId);
        Assert.Null(store.CreatedRequest?.DepartmentId);
    }

    [Fact]
    public async Task CreateUserAsync_ForCompanyAdminWithAssignment_ReturnsValidationError()
    {
        var service = CreateCompanyAdminService();
        var request = ValidRequest("CompanyAdmin");
        request.MarketId = 3;

        var result = await service.CreateUserAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal("CompanyAdmin cannot be assigned to a market or department.", result.Message);
    }

    [Fact]
    public async Task CreateUserAsync_ForDepartmentWithoutMarket_ReturnsValidationError()
    {
        var service = CreateCompanyAdminService();
        var request = ValidRequest("InventoryEmployee");
        request.DepartmentId = 4;

        var result = await service.CreateUserAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal("Department assignment requires market assignment.", result.Message);
    }

    private static UserService CreateCompanyAdminService()
    {
        return new UserService(
            new FakeUserStore(),
            new FakeCurrentUserService { CompanyId = 12, Role = "CompanyAdmin" });
    }

    private static CreateUserRequest ValidRequest(string roleName)
    {
        return new CreateUserRequest
        {
            FullName = "Store User",
            Email = "user@freshmarket.test",
            Password = "User12345",
            RoleName = roleName
        };
    }

    private sealed class FakeUserStore : IUserStore
    {
        public int? CreatedCompanyId { get; private set; }

        public CreateUserRequest? CreatedRequest { get; private set; }

        public Task<IReadOnlyCollection<UserDto>> GetUsersAsync(
            int? companyId,
            bool includeAllCompanies,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyCollection<UserDto> users = Array.Empty<UserDto>();
            return Task.FromResult(users);
        }

        public Task<UserDto?> CreateUserAsync(
            int companyId,
            CreateUserRequest request,
            CancellationToken cancellationToken = default)
        {
            CreatedCompanyId = companyId;
            CreatedRequest = request;

            return Task.FromResult<UserDto?>(new UserDto
            {
                Id = 1,
                FullName = request.FullName,
                Email = request.Email,
                RoleName = request.RoleName,
                IsActive = request.IsActive
            });
        }

        public Task<UserDto?> UpdateUserAsync(
            int id,
            int? companyId,
            bool includeAllCompanies,
            UpdateUserRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<UserDto?>(null);
        }

        public Task<UserDto?> PatchUserAsync(
            int id,
            int? companyId,
            bool includeAllCompanies,
            PatchUserRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<UserDto?>(null);
        }

        public Task<bool> DeleteUserAsync(
            int id,
            int? companyId,
            bool includeAllCompanies,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }

    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public int? UserId { get; init; } = 1;

        public int? CompanyId { get; init; }

        public string? Email { get; init; } = "admin@freshmarket.test";

        public string? Role { get; init; }

        public string? SchemaName { get; init; } = "fresh_market";
    }
}
