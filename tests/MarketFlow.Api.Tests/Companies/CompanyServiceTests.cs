using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Features.Companies.DTOs;
using MarketFlow.Application.Features.Companies.Services;

namespace MarketFlow.Api.Tests.Companies;

public sealed class CompanyServiceTests
{
    [Fact]
    public async Task CreateCompanyAsync_WithValidRequest_CreatesCompany()
    {
        var store = new FakeCompanyStore();
        var service = new CompanyService(store);

        var result = await service.CreateCompanyAsync(new CreateCompanyRequest
        {
            Name = "Fresh Market",
            CompanyType = "small",
            CompanyAdmin = new CreateCompanyAdminRequest
            {
                FullName = "Fresh Admin",
                Email = "Admin@FreshMarket.test",
                Password = "Admin12345"
            }
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Fresh Market", result.Data.Company.Name);
        Assert.Equal("SMALL", result.Data.Company.CompanyType);
        Assert.Equal("fresh_market", result.Data.Company.SchemaName);
        Assert.Equal("BASIC", result.Data.Company.SubscriptionPlan);
        Assert.Equal(5, result.Data.Company.MaxMarkets);
        Assert.Equal(50, result.Data.Company.MaxUsers);
        Assert.True(result.Data.Company.IsActive);
        Assert.NotEqual(default, result.Data.Company.CreatedAt);
        Assert.Equal("Fresh Admin", result.Data.CompanyAdmin.FullName);
        Assert.Equal("admin@freshmarket.test", result.Data.CompanyAdmin.Email);
        Assert.Equal("CompanyAdmin", result.Data.CompanyAdmin.RoleName);
        Assert.Single(store.CreatedCompanies);
    }

    [Theory]
    [InlineData("1tenant")]
    [InlineData("tenant-name")]
    [InlineData("tenant.name")]
    [InlineData("tenant name")]
    public async Task CreateCompanyAsync_WithInvalidSchemaName_ReturnsFailure(string schemaName)
    {
        var service = new CompanyService(new FakeCompanyStore());

        var result = await service.CreateCompanyAsync(new CreateCompanyRequest
        {
            Name = "Fresh Market",
            CompanyType = "SMALL",
            SchemaName = schemaName,
            CompanyAdmin = ValidAdminRequest()
        });

        Assert.False(result.Succeeded);
        Assert.Contains("Schema name", result.Message);
    }

    [Fact]
    public async Task CreateCompanyAsync_WithDuplicateSchemaName_ReturnsFailure()
    {
        var store = new FakeCompanyStore();
        store.ExistingSchemaNames.Add("fresh_market");
        var service = new CompanyService(store);

        var result = await service.CreateCompanyAsync(new CreateCompanyRequest
        {
            Name = "Fresh Market",
            CompanyType = "SMALL",
            CompanyAdmin = ValidAdminRequest()
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Schema name is already used.", result.Message);
        Assert.Empty(store.CreatedCompanies);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ENTERPRISE")]
    public async Task CreateCompanyAsync_WithInvalidCompanyType_ReturnsFailure(string companyType)
    {
        var service = new CompanyService(new FakeCompanyStore());

        var result = await service.CreateCompanyAsync(new CreateCompanyRequest
        {
            Name = "Fresh Market",
            CompanyType = companyType,
            CompanyAdmin = ValidAdminRequest()
        });

        Assert.False(result.Succeeded);
        Assert.Contains("Company type", result.Message);
    }

    [Fact]
    public async Task CreateCompanyAsync_WithDuplicateAdminEmail_ReturnsFailure()
    {
        var store = new FakeCompanyStore();
        store.ExistingEmails.Add("admin@freshmarket.test");
        var service = new CompanyService(store);

        var result = await service.CreateCompanyAsync(new CreateCompanyRequest
        {
            Name = "Fresh Market",
            CompanyType = "SMALL",
            CompanyAdmin = ValidAdminRequest()
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Company admin email is already used.", result.Message);
        Assert.Empty(store.CreatedCompanies);
    }

    [Fact]
    public async Task CreateCompanyAsync_WhenCompanyAdminRoleIsMissing_ReturnsFailure()
    {
        var store = new FakeCompanyStore { CompanyAdminRoleExists = false };
        var service = new CompanyService(store);

        var result = await service.CreateCompanyAsync(new CreateCompanyRequest
        {
            Name = "Fresh Market",
            CompanyType = "SMALL",
            CompanyAdmin = ValidAdminRequest()
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Company could not be created.", result.Message);
        Assert.Empty(store.CreatedCompanies);
    }

    private static CreateCompanyAdminRequest ValidAdminRequest()
    {
        return new CreateCompanyAdminRequest
        {
            FullName = "Fresh Admin",
            Email = "admin@freshmarket.test",
            Password = "Admin12345"
        };
    }

    private sealed class FakeCompanyStore : ICompanyStore
    {
        public HashSet<string> ExistingSchemaNames { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ExistingEmails { get; } = new(StringComparer.Ordinal);

        public List<CompanyDto> CreatedCompanies { get; } = new();

        public bool CompanyAdminRoleExists { get; set; } = true;

        public Task<IReadOnlyCollection<CompanyDto>> GetCompaniesAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<CompanyDto>>(CreatedCompanies);
        }

        public Task<CompanyDto?> GetCompanyByIdAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreatedCompanies.FirstOrDefault(x => x.Id == id));
        }

        public Task<bool> SchemaNameExistsAsync(
            string schemaName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistingSchemaNames.Contains(schemaName));
        }

        public Task<bool> EmailExistsAsync(
            string email,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistingEmails.Contains(NormalizeEmail(email)));
        }

        public Task<CompanyOnboardingDto?> CreateCompanyAsync(
            CreateCompanyRequest request,
            string schemaName,
            CancellationToken cancellationToken = default)
        {
            if (!CompanyAdminRoleExists)
            {
                return Task.FromResult<CompanyOnboardingDto?>(null);
            }

            var normalizedAdminEmail = NormalizeEmail(request.CompanyAdmin.Email);
            var company = new CompanyDto
            {
                Id = CreatedCompanies.Count + 1,
                Name = request.Name,
                SchemaName = schemaName,
                CompanyType = request.CompanyType,
                SubscriptionPlan = "BASIC",
                MaxMarkets = 5,
                MaxUsers = 50,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };

            CreatedCompanies.Add(company);
            ExistingSchemaNames.Add(schemaName);
            ExistingEmails.Add(normalizedAdminEmail);

            var response = new CompanyOnboardingDto
            {
                Company = company,
                CompanyAdmin = new CompanyAdminSummaryDto
                {
                    Id = CreatedCompanies.Count,
                    FullName = request.CompanyAdmin.FullName,
                    Email = normalizedAdminEmail,
                    RoleName = "CompanyAdmin",
                    IsActive = true
                }
            };

            return Task.FromResult<CompanyOnboardingDto?>(response);
        }

        private static string NormalizeEmail(string email)
        {
            return email.Trim().ToLowerInvariant();
        }
    }
}
