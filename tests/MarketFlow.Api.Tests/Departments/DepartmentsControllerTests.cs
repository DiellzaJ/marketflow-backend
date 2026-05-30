using MarketFlow.Api.Controllers;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Departments.DTOs;
using MarketFlow.Application.Features.Departments.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MarketFlow.Api.Tests.Departments;

public sealed class DepartmentsControllerTests
{
    [Fact]
    public async Task GetAsync_ReturnsDepartmentsFromService()
    {
        var departments = new List<DepartmentDto>
        {
            new() { Id = 7, MarketId = 3, Name = "Produce", IsActive = true }
        };
        var service = new StubDepartmentService(departments);
        var controller = new DepartmentsController(service);

        var response = await controller.GetAsync(marketId: 3, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var result = Assert.IsType<ServiceResult<IReadOnlyCollection<DepartmentDto>>>(okResult.Value);
        Assert.Same(departments, result.Data);
        Assert.Equal(3, service.LastMarketId);
    }

    [Fact]
    public async Task GetByIdAsync_WhenDepartmentExists_ReturnsOk()
    {
        var department = CreateDepartment();
        var controller = new DepartmentsController(new StubDepartmentService(department));

        var response = await controller.GetByIdAsync(department.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Same(department, Assert.IsType<ServiceResult<DepartmentDto>>(ok.Value).Data);
    }

    [Fact]
    public async Task CreateAsync_ReturnsCreatedAtNamedDepartmentRoute()
    {
        var department = CreateDepartment();
        var controller = new DepartmentsController(new StubDepartmentService(department));

        var response = await controller.CreateAsync(
            new CreateDepartmentRequest
            {
                MarketId = department.MarketId,
                Name = department.Name
            },
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtRouteResult>(response.Result);
        Assert.Equal("GetDepartmentById", created.RouteName);
        Assert.Equal(department.Id, created.RouteValues?["id"]);
        Assert.Same(department, Assert.IsType<ServiceResult<DepartmentDto>>(created.Value).Data);
    }

    [Fact]
    public async Task UpdateAsync_WhenDepartmentIsMissing_ReturnsNotFound()
    {
        var result = ServiceResult<DepartmentDto>.Failure(
            "Department was not found.",
            ServiceResultFailureType.NotFound);
        var controller = new DepartmentsController(new StubDepartmentService(result));

        var response = await controller.UpdateAsync(10, new UpdateDepartmentRequest(), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(response.Result);
        Assert.Same(result, notFound.Value);
    }

    [Fact]
    public async Task DeactivateAsync_WhenDepartmentIsAlreadyInactive_ReturnsConflict()
    {
        var result = ServiceResult<DepartmentDto>.Failure(
            "Department is already inactive.",
            ServiceResultFailureType.Conflict);
        var controller = new DepartmentsController(new StubDepartmentService(result));

        var response = await controller.DeactivateAsync(10, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(response.Result);
        Assert.Same(result, conflict.Value);
    }

    private static DepartmentDto CreateDepartment()
    {
        return new DepartmentDto
        {
            Id = 7,
            MarketId = 3,
            Name = "Produce",
            Description = "Fresh section",
            IsActive = true
        };
    }

    private sealed class StubDepartmentService : IDepartmentService
    {
        private readonly ServiceResult<DepartmentDto> _result;
        private readonly IReadOnlyCollection<DepartmentDto> _departments;

        public StubDepartmentService(DepartmentDto department)
        {
            _result = ServiceResult<DepartmentDto>.Success(department, "Department created.");
            _departments = [department];
        }

        public StubDepartmentService(IReadOnlyCollection<DepartmentDto> departments)
        {
            _departments = departments;
            _result = ServiceResult<DepartmentDto>.Success(departments.First(), "Department created.");
        }

        public StubDepartmentService(ServiceResult<DepartmentDto> result)
        {
            _result = result;
            _departments = result.Data is null ? [] : [result.Data];
        }

        public int? LastMarketId { get; private set; }

        public Task<ServiceResult<IReadOnlyCollection<DepartmentDto>>> GetDepartmentsAsync(
            int? marketId = null,
            CancellationToken cancellationToken = default)
        {
            LastMarketId = marketId;

            if (!_result.Succeeded)
            {
                return Task.FromResult(ServiceResult<IReadOnlyCollection<DepartmentDto>>.Failure(
                    _result.Message,
                    _result.FailureType));
            }

            return Task.FromResult(ServiceResult<IReadOnlyCollection<DepartmentDto>>.Success(_departments));
        }

        public Task<ServiceResult<DepartmentDto>> GetDepartmentAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }

        public Task<ServiceResult<DepartmentDto>> CreateDepartmentAsync(
            CreateDepartmentRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }

        public Task<ServiceResult<DepartmentDto>> UpdateDepartmentAsync(
            int id,
            UpdateDepartmentRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }

        public Task<ServiceResult<DepartmentDto>> DeactivateDepartmentAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }

        public Task<ServiceResult<DepartmentDto>> ActivateDepartmentAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }
}
