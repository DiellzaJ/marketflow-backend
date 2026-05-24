using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Features.Users.Configuration;
using MarketFlow.Application.Features.Users.DTOs;
using MarketFlow.Application.Features.Users.Services;

namespace MarketFlow.Api.Tests.Users;

public sealed class UserServiceTests
{
    [Fact]
    public async Task CreateUserAsync_ForCompanyAdmin_DerivesCompanyFromCurrentUser()
    {
        var store = new FakeUserStore();
        var service = CreateUserService(
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
        var service = CreateUserService(
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
        var service = CreateUserService(
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
    [InlineData("InventoryEmployee")]
    public async Task CreateUserAsync_ForMarketRolesWithoutMarket_ReturnsValidationError(string roleName)
    {
        var service = CreateCompanyAdminService();

        var result = await service.CreateUserAsync(ValidRequest(roleName));

        Assert.False(result.Succeeded);
        Assert.Equal($"{RoleAssignmentRules.NormalizeRoleName(roleName)} requires market assignment.", result.Message);
    }

    [Fact]
    public async Task CreateUserAsync_ForDepartmentManagerWithoutMarket_ReturnsValidationError()
    {
        var service = CreateCompanyAdminService();

        var result = await service.CreateUserAsync(ValidRequest("DepartmentManager"));

        Assert.False(result.Succeeded);
        Assert.Equal("DepartmentManager requires market and department assignment.", result.Message);
    }

    [Theory]
    [InlineData("Seller")]
    [InlineData("MainOperator")]
    [InlineData("DepartmentManager")]
    [InlineData("InventoryEmployee")]
    public async Task CreateUserAsync_ForMarketRolesWithDepartment_CreatesUserSuccessfully(string roleName)
    {
        var store = new FakeUserStore();
        var service = CreateCompanyAdminService(store);
        var request = ValidRequest(roleName);
        request.MarketId = 3;
        request.DepartmentId = 4;

        var result = await service.CreateUserAsync(request);

        Assert.True(result.Succeeded);
        Assert.Equal(3, store.CreatedRequest?.MarketId);
        Assert.Equal(4, store.CreatedRequest?.DepartmentId);
        Assert.Equal(3, result.Data?.Assignment?.MarketId);
        Assert.Equal(4, result.Data?.Assignment?.DepartmentId);
    }

    [Fact]
    public async Task CreateUserAsync_ForSellerWithValidMarket_CreatesUserSuccessfully()
    {
        var store = new FakeUserStore();
        var service = CreateCompanyAdminService(store);
        var request = ValidRequest("Seller");
        request.MarketId = 3;

        var result = await service.CreateUserAsync(request);

        Assert.True(result.Succeeded);
        Assert.Equal(3, store.CreatedRequest?.MarketId);
        Assert.Null(store.CreatedRequest?.DepartmentId);
        Assert.Equal(3, result.Data?.Assignment?.MarketId);
        Assert.Null(result.Data?.Assignment?.DepartmentId);
    }

    [Theory]
    [InlineData("Seller")]
    [InlineData("MainOperator")]
    [InlineData("InventoryEmployee")]
    public async Task CreateUserAsync_ForOperationalRoleWithNoDepartment_CreatesMarketAssignment(
        string roleName)
    {
        var store = new FakeUserStore();
        var service = CreateCompanyAdminService(store);
        var request = ValidRequest(roleName);
        request.MarketId = 3;

        var result = await service.CreateUserAsync(request);

        Assert.True(result.Succeeded);
        Assert.Equal(3, store.CreatedRequest?.MarketId);
        Assert.Null(store.CreatedRequest?.DepartmentId);
        Assert.Equal(3, result.Data?.Assignment?.MarketId);
        Assert.Null(result.Data?.Assignment?.DepartmentId);
    }

    [Fact]
    public async Task CreateUserAsync_ForDepartmentManagerWithoutDepartment_ReturnsValidationError()
    {
        var service = CreateCompanyAdminService();
        var request = ValidRequest("DepartmentManager");
        request.MarketId = 3;

        var result = await service.CreateUserAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal("DepartmentManager requires department assignment.", result.Message);
    }

    [Theory]
    [InlineData("seller")]
    [InlineData("SELLER")]
    [InlineData("SeLlEr")]
    public async Task CreateUserAsync_IsCaseInsensitiveForRoleNames(string roleName)
    {
        var store = new FakeUserStore();
        var service = CreateCompanyAdminService(store);
        var request = ValidRequest(roleName);
        request.MarketId = 3;

        var result = await service.CreateUserAsync(request);

        Assert.True(result.Succeeded);
        Assert.Equal("Seller", result.Data?.RoleName);
    }

    [Fact]
    public async Task CreateUserAsync_ForCompanyAdminWithNoAssignment_CreatesUser()
    {
        var store = new FakeUserStore();
        var service = CreateUserService(
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
        Assert.Equal("InventoryEmployee requires market assignment.", result.Message);
    }

    [Fact]
    public async Task GetUsersAsync_ReturnsAssignmentSummary()
    {
        var store = new FakeUserStore();
        store.Users.Add(new UserDto
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
        });
        var service = CreateCompanyAdminService(store);

        var result = await service.GetUsersAsync();

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        var user = Assert.Single(result.Data);
        Assert.Equal(3, user.Assignment?.MarketId);
        Assert.Equal("Central Market", user.Assignment?.MarketName);
        Assert.Equal(4, user.Assignment?.DepartmentId);
        Assert.Equal("Produce", user.Assignment?.DepartmentName);
    }

    [Fact]
    public async Task GetUsersAsync_ReturnsAssignmentSummaryWhenDepartmentIsMissing()
    {
        var store = new FakeUserStore();
        store.Users.Add(new UserDto
        {
            Id = 1,
            FullName = "Store Seller",
            Email = "seller@freshmarket.test",
            RoleName = "Seller",
            IsActive = true,
            Assignment = new UserAssignmentSummaryDto
            {
                MarketId = 3,
                MarketName = "Central Market"
            }
        });
        var service = CreateCompanyAdminService(store);

        var result = await service.GetUsersAsync();

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        var user = Assert.Single(result.Data);
        Assert.Equal(3, user.Assignment?.MarketId);
        Assert.Equal("Central Market", user.Assignment?.MarketName);
        Assert.Null(user.Assignment?.DepartmentId);
        Assert.Null(user.Assignment?.DepartmentName);
    }

    [Fact]
    public async Task GetUsersAsync_ReturnsLargeAssignmentBatch()
    {
        var store = new FakeUserStore();

        for (var id = 1; id <= 250; id++)
        {
            store.Users.Add(new UserDto
            {
                Id = id,
                FullName = $"Store User {id:D3}",
                Email = $"user{id:D3}@freshmarket.test",
                RoleName = "Seller",
                IsActive = true,
                Assignment = new UserAssignmentSummaryDto
                {
                    MarketId = 3,
                    MarketName = "Central Market"
                }
            });
        }

        var service = CreateCompanyAdminService(store);

        var result = await service.GetUsersAsync();

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(250, result.Data.Count);
        Assert.All(result.Data, user =>
        {
            Assert.Equal(3, user.Assignment?.MarketId);
            Assert.Null(user.Assignment?.DepartmentId);
        });
    }

    [Fact]
    public async Task CreateUserAsync_WithMarketThatDoesNotExist_ReturnsValidationError()
    {
        var store = new FakeUserStore();
        store.ExistingMarketIds.Clear();
        var service = CreateCompanyAdminService(store);
        var request = ValidRequest("Seller");
        request.MarketId = 99;

        var result = await service.CreateUserAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal("Market ID 99 not found.", result.Message);
    }

    [Fact]
    public async Task CreateUserAsync_WithDepartmentThatDoesNotExist_ReturnsValidationError()
    {
        var store = new FakeUserStore();
        store.ExistingDepartmentIds.Clear();
        var service = CreateCompanyAdminService(store);
        var request = ValidRequest("DepartmentManager");
        request.MarketId = 3;
        request.DepartmentId = 44;

        var result = await service.CreateUserAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal("Department ID 44 not found in market 3.", result.Message);
    }

    [Fact]
    public async Task CreateUserAsync_WithStaffAssignment_StoresMarketAndDepartmentIds()
    {
        var store = new FakeUserStore();
        var service = CreateCompanyAdminService(store);
        var request = ValidRequest("DepartmentManager");
        request.MarketId = 3;
        request.DepartmentId = 4;

        var result = await service.CreateUserAsync(request);

        Assert.True(result.Succeeded);
        Assert.Equal(3, store.CreatedRequest?.MarketId);
        Assert.Equal(4, store.CreatedRequest?.DepartmentId);
    }

    private static UserService CreateCompanyAdminService(FakeUserStore? store = null)
    {
        return CreateUserService(
            store ?? new FakeUserStore(),
            new FakeCurrentUserService { CompanyId = 12, Role = "CompanyAdmin" });
    }

    private static UserService CreateUserService(
        FakeUserStore store,
        FakeCurrentUserService currentUserService)
    {
        return new UserService(
            store,
            currentUserService,
            new UserCreationValidator(store));
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

        public HashSet<int> ExistingMarketIds { get; } = new() { 3 };

        public HashSet<(int MarketId, int DepartmentId)> ExistingDepartmentIds { get; } = new()
        {
            (3, 4)
        };

        public List<UserDto> Users { get; } = [];

        public Task<IReadOnlyCollection<UserDto>> GetUsersAsync(
            int? companyId,
            bool includeAllCompanies,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<UserDto>>(Users);
        }

        public Task<UserDto?> GetUserAsync(
            int id,
            int? companyId,
            bool includeAllCompanies,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Users.FirstOrDefault(x => x.Id == id));
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
                RoleName = RoleAssignmentRules.NormalizeRoleName(request.RoleName),
                IsActive = request.IsActive,
                Assignment = request.MarketId.HasValue
                    ? new UserAssignmentSummaryDto
                    {
                        MarketId = request.MarketId.Value,
                        MarketName = $"Market {request.MarketId.Value}",
                        DepartmentId = request.DepartmentId,
                        DepartmentName = request.DepartmentId.HasValue
                            ? $"Department {request.DepartmentId.Value}"
                            : null
                    }
                    : null
            });
        }

        public Task<bool> MarketExistsAsync(
            int companyId,
            int marketId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistingMarketIds.Contains(marketId));
        }

        public Task<bool> DepartmentExistsAsync(
            int companyId,
            int marketId,
            int departmentId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistingDepartmentIds.Contains((marketId, departmentId)));
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

        public Task<bool> ChangePasswordAsync(
            int id,
            string currentPassword,
            string newPassword,
            int? companyId,
            bool includeAllCompanies,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
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
