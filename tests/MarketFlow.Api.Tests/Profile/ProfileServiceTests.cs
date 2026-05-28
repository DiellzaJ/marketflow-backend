using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Features.Companies.DTOs;
using MarketFlow.Application.Features.Profile.DTOs;
using MarketFlow.Application.Features.Profile.Services;
using MarketFlow.Application.Features.Users.DTOs;

namespace MarketFlow.Api.Tests.Profile;

public sealed class ProfileServiceTests
{
    [Fact]
    public async Task GetProfileAsync_ReturnsProfileForCurrentUser()
    {
        var store = new FakeUserStore();
        var companyStore = new FakeCompanyStore();
        var current = new FakeCurrentUserService { UserId = 1, CompanyId = 2 };
        companyStore.Companies.Add(new CompanyDto { Id = 2, Name = "Test Company" });
        store.Users.Add(new UserDto
        {
            Id = 1,
            FullName = "Test User",
            Email = "test@x.test",
            RoleName = "Seller",
            IsActive = true,
            Assignment = new MarketFlow.Application.Features.Users.DTOs.UserAssignmentSummaryDto
            {
                MarketId = 5,
                DepartmentId = 7
            }
        });

        var service = new ProfileService(store, current, companyStore);

        var result = await service.GetProfileAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Data?.Id);
        Assert.Equal("Test User", result.Data?.FullName);
        Assert.Equal(5, result.Data?.MarketId);
        Assert.Equal(7, result.Data?.DepartmentId);
        Assert.Equal(2, result.Data?.CompanyId);
        Assert.Equal("Test Company", result.Data?.CompanyName);
    }

    [Fact]
    public async Task GetProfileAsync_ReturnsCompanyNameEvenWithoutAssignment()
    {
        var store = new FakeUserStore();
        var companyStore = new FakeCompanyStore();
        var current = new FakeCurrentUserService { UserId = 1, CompanyId = 2 };
        companyStore.Companies.Add(new CompanyDto { Id = 2, Name = "Acme Corp" });
        store.Users.Add(new UserDto
        {
            Id = 1,
            FullName = "Admin User",
            Email = "admin@x.test",
            RoleName = "CompanyAdmin",
            IsActive = true,
            Assignment = null
        });

        var service = new ProfileService(store, current, companyStore);

        var result = await service.GetProfileAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data?.CompanyId);
        Assert.Equal("Acme Corp", result.Data?.CompanyName);
        Assert.Null(result.Data?.MarketId);
        Assert.Null(result.Data?.DepartmentId);
        Assert.Null(result.Data?.MarketName);
        Assert.Null(result.Data?.DepartmentName);
    }

    [Fact]
    public async Task UpdateProfileAsync_UpdatesFullName()
    {
        var store = new FakeUserStore();
        var companyStore = new FakeCompanyStore();
        var current = new FakeCurrentUserService { UserId = 1, CompanyId = 2 };
        store.Users.Add(new UserDto { Id = 1, FullName = "Old Name", Email = "a@b.test", RoleName = "Seller", IsActive = true });
        var service = new ProfileService(store, current, companyStore);

        var result = await service.UpdateProfileAsync(new UpdateProfileRequest { FullName = "New Name" });

        Assert.True(result.Succeeded);
        Assert.Equal("New Name", store.Users.First().FullName);
    }

    [Fact]
    public async Task UpdateProfileAsync_DoesNotUpdateRestrictedFields()
    {
        var store = new FakeUserStore();
        var companyStore = new FakeCompanyStore();
        var current = new FakeCurrentUserService { UserId = 1, CompanyId = 2 };
        store.Users.Add(new UserDto { Id = 1, FullName = "Old", Email = "a@b.test", RoleName = "Seller", IsActive = true });

        MarketFlow.Application.Features.Users.DTOs.PatchUserRequest? received = null;

        // Replace PatchUserAsync to capture the request
        store.OverridePatch = (id, companyId, includeAll, req, ct) =>
        {
            received = req;
            // perform normal behavior
            if (!string.IsNullOrWhiteSpace(req.FullName))
            {
                var u = store.Users.First(x => x.Id == id);
                u.FullName = req.FullName;
            }

            return Task.FromResult<UserDto?>(store.Users.First(x => x.Id == id));
        };

        var service = new ProfileService(store, current, companyStore);

        var result = await service.UpdateProfileAsync(new UpdateProfileRequest { FullName = "New" });

        Assert.True(result.Succeeded);
        Assert.NotNull(received);
        Assert.Equal("New", received!.FullName);
        // Restricted fields should not be set by ProfileService
        Assert.True(string.IsNullOrWhiteSpace(received.Email));
        Assert.True(string.IsNullOrWhiteSpace(received.RoleName));
        Assert.Null(received.IsActive);
    }

    [Fact]
    public async Task ChangePasswordAsync_Succeeds_WithCorrectCurrentPassword()
    {
        var store = new FakeUserStore();
        var current = new FakeCurrentUserService { UserId = 1, CompanyId = 2 };
        var companyStore = new FakeCompanyStore();
        store.AddUserWithPassword(1, "user@x.test", "OldPass123");

        var service = new ProfileService(store, current, companyStore);

        var result = await service.ChangePasswordAsync(new ChangePasswordRequest { CurrentPassword = "OldPass123", NewPassword = "NewPass456" });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ChangePasswordAsync_Fails_WithInvalidCurrentPassword()
    {
        var store = new FakeUserStore();
        var current = new FakeCurrentUserService { UserId = 1, CompanyId = 2 };
        var companyStore = new FakeCompanyStore();
        store.AddUserWithPassword(1, "user@x.test", "OldPass123");

        var service = new ProfileService(store, current, companyStore);

        var result = await service.ChangePasswordAsync(new ChangePasswordRequest { CurrentPassword = "WrongPass", NewPassword = "NewPass456" });

        Assert.False(result.Succeeded);
    }

    private sealed class FakeUserStore : IUserStore
    {
        public List<UserDto> Users { get; } = new();

        public Func<int, int?, bool, MarketFlow.Application.Features.Users.DTOs.PatchUserRequest, CancellationToken, Task<UserDto?>>? OverridePatch { get; set; }

        private readonly Dictionary<int, string> _passwordHashes = new();

        public void AddUserWithPassword(int id, string email, string password)
        {
            Users.Add(new UserDto { Id = id, FullName = "User", Email = email, RoleName = "Seller", IsActive = true });
            _passwordHashes[id] = BCrypt.Net.BCrypt.HashPassword(password);
        }

        public Task<IReadOnlyCollection<UserDto>> GetUsersAsync(int? companyId, bool includeAllCompanies, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<UserDto>>(Users);

        public Task<UserDto?> GetUserAsync(int id, int? companyId, bool includeAllCompanies, CancellationToken cancellationToken = default)
            => Task.FromResult(Users.FirstOrDefault(x => x.Id == id));

        public Task<UserDto?> CreateUserAsync(int companyId, MarketFlow.Application.Features.Users.DTOs.CreateUserRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult<UserDto?>(null);

        public Task<bool> CompanyExistsAsync(
            int companyId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> MarketExistsAsync(int companyId, int marketId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> DepartmentExistsAsync(int companyId, int marketId, int departmentId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<UserDto?> UpdateUserAsync(int id, int? companyId, bool includeAllCompanies, MarketFlow.Application.Features.Users.DTOs.UpdateUserRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult<UserDto?>(null);

        public Task<UserDto?> PatchUserAsync(int id, int? companyId, bool includeAllCompanies, MarketFlow.Application.Features.Users.DTOs.PatchUserRequest request, CancellationToken cancellationToken = default)
        {
            if (OverridePatch is not null)
            {
                return OverridePatch(id, companyId, includeAllCompanies, request, cancellationToken);
            }

            var user = Users.FirstOrDefault(x => x.Id == id);

            if (user is null) return Task.FromResult<UserDto?>(null);

            if (!string.IsNullOrWhiteSpace(request.FullName))
            {
                user.FullName = request.FullName;
            }

            return Task.FromResult<UserDto?>(user);
        }

        public Task<bool> DeleteUserAsync(int id, int? companyId, bool includeAllCompanies, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> ChangePasswordAsync(int id, string currentPassword, string newPassword, int? companyId, bool includeAllCompanies, CancellationToken cancellationToken = default)
        {
            if (!_passwordHashes.TryGetValue(id, out var hash))
            {
                return Task.FromResult(false);
            }

            if (!BCrypt.Net.BCrypt.Verify(currentPassword, hash))
            {
                return Task.FromResult(false);
            }

            _passwordHashes[id] = BCrypt.Net.BCrypt.HashPassword(newPassword);

            return Task.FromResult(true);
        }
    }

    private sealed class FakeCompanyStore : ICompanyStore
    {
        public List<CompanyDto> Companies { get; } = new();

        public Task<IReadOnlyCollection<CompanyDto>> GetCompaniesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<CompanyDto>>(Companies);

        public Task<CompanyDto?> GetCompanyByIdAsync(int id, CancellationToken cancellationToken = default)
            => Task.FromResult(Companies.FirstOrDefault(x => x.Id == id));

        public Task<bool> SchemaNameExistsAsync(string schemaName, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<CompanyOnboardingDto?> CreateCompanyAsync(CreateCompanyRequest request, string schemaName, CancellationToken cancellationToken = default)
            => Task.FromResult<CompanyOnboardingDto?>(null);
    }

    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public int? UserId { get; init; }

        public int? CompanyId { get; init; }

        public string? Email { get; init; }

        public string? Role { get; init; }

        public string? SchemaName { get; init; }
    }
}
