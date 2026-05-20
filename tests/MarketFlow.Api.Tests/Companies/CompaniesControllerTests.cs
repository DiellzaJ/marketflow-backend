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
        var onboarding = new CompanyOnboardingDto
        {
            Company = new CompanyDto
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
            },
            CompanyAdmin = new CompanyAdminSummaryDto
            {
                Id = 11,
                FullName = "Fresh Admin",
                Email = "admin@freshmarket.test",
                RoleName = "CompanyAdmin",
                IsActive = true
            }
        };
        var controller = new CompaniesController(
            new FakeCompanyService(ServiceResult<CompanyOnboardingDto>.Success(onboarding, "Company created.")));

        var response = await controller.CreateAsync(
            ValidRequest(),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtRouteResult>(response.Result);
        Assert.Equal("GetCompanyById", created.RouteName);
        Assert.Equal(onboarding.Company.Id, created.RouteValues?["id"]);
        var result = Assert.IsType<ServiceResult<CompanyOnboardingDto>>(created.Value);
        Assert.True(result.Succeeded);
        Assert.Equal(onboarding.Company.Id, result.Data?.Company.Id);
        Assert.Equal("CompanyAdmin", result.Data?.CompanyAdmin.RoleName);
    }

    [Fact]
    public async Task CreateAsync_WhenCompanyIsInvalid_ReturnsBadRequest()
    {
        var controller = new CompaniesController(
            new FakeCompanyService(ServiceResult<CompanyOnboardingDto>.Failure("Schema name is already used.")));

        var response = await controller.CreateAsync(
            ValidRequest(),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(response.Result);
        var result = Assert.IsType<ServiceResult<CompanyOnboardingDto>>(badRequest.Value);
        Assert.False(result.Succeeded);
    }

    private static CreateCompanyRequest ValidRequest()
    {
        return new CreateCompanyRequest
        {
            Name = "Fresh Market",
            CompanyType = "SMALL",
            CompanyAdmin = new CreateCompanyAdminRequest
            {
                FullName = "Fresh Admin",
                Email = "admin@freshmarket.test",
                Password = "Admin12345"
            }
        };
    }

    private sealed class FakeCompanyService : ICompanyService
    {
        private readonly ServiceResult<CompanyOnboardingDto> _createResult;

        public FakeCompanyService(ServiceResult<CompanyOnboardingDto> createResult)
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
            return Task.FromResult(ServiceResult<CompanyDto>.Success(_createResult.Data?.Company));
        }

        public Task<ServiceResult<CompanyOnboardingDto>> CreateCompanyAsync(
            CreateCompanyRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_createResult);
        }
    }
}
