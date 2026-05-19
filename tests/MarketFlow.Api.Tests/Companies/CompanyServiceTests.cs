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
            CompanyType = "small"
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("Fresh Market", result.Data.Name);
        Assert.Equal("SMALL", result.Data.CompanyType);
        Assert.Equal("fresh_market", result.Data.SchemaName);
        Assert.Equal("BASIC", result.Data.SubscriptionPlan);
        Assert.Equal(5, result.Data.MaxMarkets);
        Assert.Equal(50, result.Data.MaxUsers);
        Assert.True(result.Data.IsActive);
        Assert.NotEqual(default, result.Data.CreatedAt);
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
            SchemaName = schemaName
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
            CompanyType = "SMALL"
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
            CompanyType = companyType
        });

        Assert.False(result.Succeeded);
        Assert.Contains("Company type", result.Message);
    }

    private sealed class FakeCompanyStore : ICompanyStore
    {
        public HashSet<string> ExistingSchemaNames { get; } = new(StringComparer.Ordinal);

        public List<CompanyDto> CreatedCompanies { get; } = new();

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

        public Task<CompanyDto?> CreateCompanyAsync(
            CreateCompanyRequest request,
            string schemaName,
            CancellationToken cancellationToken = default)
        {
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

            return Task.FromResult<CompanyDto?>(company);
        }
    }
}
