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

    private sealed class StubDepartmentService : IDepartmentService
    {
        private readonly IReadOnlyCollection<DepartmentDto> _departments;

        public StubDepartmentService(IReadOnlyCollection<DepartmentDto> departments)
        {
            _departments = departments;
        }

        public int? LastMarketId { get; private set; }

        public Task<ServiceResult<IReadOnlyCollection<DepartmentDto>>> GetDepartmentsAsync(
            int? marketId = null,
            CancellationToken cancellationToken = default)
        {
            LastMarketId = marketId;
            return Task.FromResult(ServiceResult<IReadOnlyCollection<DepartmentDto>>.Success(_departments));
        }
    }
}
