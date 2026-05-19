using MarketFlow.Api.Controllers;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Companies.DTOs;
using MarketFlow.Application.Features.Companies.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Tests.Companies;

public sealed class CompaniesControllerTests
{
    [Fact]
    public async Task CreateAsync_WhenCompanyIsCreated_ReturnsCreatedAtGetById()
    {
        var company = new CompanyDto
        {
            Id = 7,
            Name = "Fresh Market",
            SchemaName = "fresh_market",
            CompanyType = "SMALL",
            SubscriptionPlan = "BASIC",
            MaxMarkets = 5,
            MaxUsers = 50,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var controller = new CompaniesController(
            new FakeCompanyService(ServiceResult<CompanyDto>.Success(company, "Company created.")));

        var response = await controller.CreateAsync(
            new CreateCompanyRequest { Name = "Fresh Market", CompanyType = "SMALL" },
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(response.Result);
        Assert.Equal(nameof(CompaniesController.GetByIdAsync), created.ActionName);
        Assert.Equal(company.Id, created.RouteValues?["id"]);
        var result = Assert.IsType<ServiceResult<CompanyDto>>(created.Value);
        Assert.True(result.Succeeded);
        Assert.Equal(company.Id, result.Data?.Id);
    }

    [Fact]
    public async Task CreateAsync_WhenCompanyIsInvalid_ReturnsBadRequest()
    {
        var controller = new CompaniesController(
            new FakeCompanyService(ServiceResult<CompanyDto>.Failure("Schema name is already used.")));

        var response = await controller.CreateAsync(
            new CreateCompanyRequest { Name = "Fresh Market", CompanyType = "SMALL" },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(response.Result);
        var result = Assert.IsType<ServiceResult<CompanyDto>>(badRequest.Value);
        Assert.False(result.Succeeded);
    }

    private sealed class FakeCompanyService : ICompanyService
    {
        private readonly ServiceResult<CompanyDto> _createResult;

        public FakeCompanyService(ServiceResult<CompanyDto> createResult)
        {
            _createResult = createResult;
        }

        public Task<ServiceResult<IReadOnlyCollection<CompanyDto>>> GetCompaniesAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyCollection<CompanyDto> companies = Array.Empty<CompanyDto>();
            return Task.FromResult(ServiceResult<IReadOnlyCollection<CompanyDto>>.Success(companies));
        }

        public Task<ServiceResult<CompanyDto>> GetCompanyByIdAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_createResult);
        }

        public Task<ServiceResult<CompanyDto>> CreateCompanyAsync(
            CreateCompanyRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_createResult);
        }
    }
}
