using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Departments.DTOs;
using MarketFlow.Application.Features.Departments.Interfaces;
using MarketFlow.Application.Features.Departments.Services;

namespace MarketFlow.Api.Tests.Departments;

public sealed class DepartmentServiceTests
{
    [Fact]
    public async Task GetDepartmentsAsync_ReturnsDepartmentsFromQueryService()
    {
        var expected = new List<DepartmentDto>
        {
            new()
            {
                Id = 2,
                MarketId = 1,
                Name = "Bakery",
                Description = "Fresh baked goods",
                IsActive = true
            }
        };
        var queryService = new StubDepartmentQueryService(expected);
        var service = new DepartmentService(queryService);

        var result = await service.GetDepartmentsAsync(marketId: 1);

        Assert.True(result.Succeeded);
        Assert.Same(expected, result.Data);
        Assert.Equal(1, queryService.LastMarketId);
    }

    private sealed class StubDepartmentQueryService : IDepartmentQueryService
    {
        private readonly IReadOnlyCollection<DepartmentDto> _departments;

        public StubDepartmentQueryService(IReadOnlyCollection<DepartmentDto> departments)
        {
            _departments = departments;
        }

        public int? LastMarketId { get; private set; }

        public Task<IReadOnlyCollection<DepartmentDto>> GetDepartmentsAsync(
            int? marketId = null,
            CancellationToken cancellationToken = default)
        {
            LastMarketId = marketId;
            return Task.FromResult(_departments);
        }
    }
}
