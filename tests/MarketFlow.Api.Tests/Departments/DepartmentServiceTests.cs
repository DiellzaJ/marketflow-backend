using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Departments.DTOs;
using MarketFlow.Application.Features.Departments.Exceptions;
using MarketFlow.Application.Features.Departments.Interfaces;
using MarketFlow.Application.Features.Departments.Services;

namespace MarketFlow.Api.Tests.Departments;

public sealed class DepartmentServiceTests
{
    [Fact]
    public async Task GetDepartmentsAsync_ReturnsDepartmentsFromStore()
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
        var store = new RecordingDepartmentStore(expected);
        var service = new DepartmentService(store);

        var result = await service.GetDepartmentsAsync(marketId: 1);

        Assert.True(result.Succeeded);
        Assert.Same(expected, result.Data);
        Assert.Equal(1, store.LastMarketId);
    }

    [Fact]
    public async Task GetDepartmentAsync_WhenDepartmentIsInactive_ReturnsNotFoundByDefault()
    {
        var store = new RecordingDepartmentStore
        {
            CurrentDepartment = new DepartmentDto
            {
                Id = 6,
                MarketId = 2,
                Name = "Frozen",
                IsActive = false
            }
        };
        var service = new DepartmentService(store);

        var result = await service.GetDepartmentAsync(6);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.NotFound, result.FailureType);
        Assert.Equal("Department was not found.", result.Message);
    }

    [Fact]
    public async Task CreateDepartmentAsync_RejectsMissingMarketId()
    {
        var store = new RecordingDepartmentStore();
        var service = new DepartmentService(store);

        var result = await service.CreateDepartmentAsync(new CreateDepartmentRequest
        {
            MarketId = 0,
            Name = "Produce"
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Department marketId is required.", result.Message);
        Assert.False(store.CreateDepartmentWasCalled);
    }

    [Fact]
    public async Task CreateDepartmentAsync_RejectsMissingName()
    {
        var store = new RecordingDepartmentStore();
        var service = new DepartmentService(store);

        var result = await service.CreateDepartmentAsync(new CreateDepartmentRequest
        {
            MarketId = 2,
            Name = " "
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Department name is required.", result.Message);
        Assert.False(store.CreateDepartmentWasCalled);
    }

    [Fact]
    public async Task CreateDepartmentAsync_RejectsUnknownMarket()
    {
        var store = new RecordingDepartmentStore { MarketExists = false };
        var service = new DepartmentService(store);

        var result = await service.CreateDepartmentAsync(new CreateDepartmentRequest
        {
            MarketId = 2,
            Name = "Produce"
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.NotFound, result.FailureType);
        Assert.Equal("Department market was not found.", result.Message);
        Assert.False(store.CreateDepartmentWasCalled);
    }

    [Fact]
    public async Task CreateDepartmentAsync_RejectsDuplicateNameInSameMarket()
    {
        var store = new RecordingDepartmentStore();
        store.ExistingDepartmentNames[(4, "Produce")] = 8;
        var service = new DepartmentService(store);

        var result = await service.CreateDepartmentAsync(new CreateDepartmentRequest
        {
            MarketId = 4,
            Name = " Produce "
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.Conflict, result.FailureType);
        Assert.Equal(
            "Department name is already used by another department in this market.",
            result.Message);
        Assert.False(store.CreateDepartmentWasCalled);
    }

    [Fact]
    public async Task CreateDepartmentAsync_TrimsPayload()
    {
        var store = new RecordingDepartmentStore();
        var service = new DepartmentService(store);

        var result = await service.CreateDepartmentAsync(new CreateDepartmentRequest
        {
            MarketId = 4,
            Name = " Produce ",
            Description = " Fresh section "
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Department created.", result.Message);
        Assert.True(store.CreateDepartmentWasCalled);
        Assert.Equal("Produce", result.Data?.Name);
        Assert.Equal("Fresh section", result.Data?.Description);
    }

    [Fact]
    public async Task CreateDepartmentAsync_WhenStoreReportsMissingMarket_ReturnsNotFound()
    {
        var store = new RecordingDepartmentStore
        {
            ThrowMarketNotFoundOnCreate = true
        };
        var service = new DepartmentService(store);

        var result = await service.CreateDepartmentAsync(new CreateDepartmentRequest
        {
            MarketId = 4,
            Name = "Produce"
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.NotFound, result.FailureType);
        Assert.Equal("Department market was not found.", result.Message);
    }

    [Fact]
    public async Task UpdateDepartmentAsync_RejectsActiveStateChange()
    {
        var store = new RecordingDepartmentStore();
        var service = new DepartmentService(store);

        var result = await service.UpdateDepartmentAsync(
            6,
            new UpdateDepartmentRequest
            {
                Name = "Produce",
                IsActive = false
            });

        Assert.False(result.Succeeded);
        Assert.Equal(
            "Use the department deactivate or activate endpoint to change active state.",
            result.Message);
        Assert.False(store.UpdateDepartmentWasCalled);
    }

    [Fact]
    public async Task UpdateDepartmentAsync_AllowsCurrentDepartmentName()
    {
        var store = new RecordingDepartmentStore();
        store.ExistingDepartmentNames[(3, "Produce")] = 6;
        var service = new DepartmentService(store);

        var result = await service.UpdateDepartmentAsync(6, new UpdateDepartmentRequest
        {
            Name = "Produce"
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Department updated.", result.Message);
        Assert.True(store.UpdateDepartmentWasCalled);
    }

    [Fact]
    public async Task DeactivateDepartmentAsync_WhenDepartmentIsActive_DeactivatesDepartment()
    {
        var store = new RecordingDepartmentStore();
        var service = new DepartmentService(store);

        var result = await service.DeactivateDepartmentAsync(6);

        Assert.True(result.Succeeded);
        Assert.Equal("Department deactivated.", result.Message);
        Assert.True(store.SetDepartmentActiveStateWasCalled);
        Assert.False(store.LastActiveState);
        Assert.False(result.Data?.IsActive);
    }

    [Fact]
    public async Task ActivateDepartmentAsync_WhenDepartmentIsAlreadyActive_ReturnsConflict()
    {
        var store = new RecordingDepartmentStore();
        var service = new DepartmentService(store);

        var result = await service.ActivateDepartmentAsync(6);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.Conflict, result.FailureType);
        Assert.Equal("Department is already active.", result.Message);
        Assert.False(store.SetDepartmentActiveStateWasCalled);
    }

    private sealed class RecordingDepartmentStore : IDepartmentStore
    {
        private readonly IReadOnlyCollection<DepartmentDto> _departments;

        public RecordingDepartmentStore()
            : this([])
        {
        }

        public RecordingDepartmentStore(IReadOnlyCollection<DepartmentDto> departments)
        {
            _departments = departments;
        }

        public int? LastMarketId { get; private set; }

        public bool MarketExists { get; set; } = true;

        public bool CreateDepartmentWasCalled { get; private set; }

        public bool UpdateDepartmentWasCalled { get; private set; }

        public bool SetDepartmentActiveStateWasCalled { get; private set; }

        public bool LastActiveState { get; private set; }

        public bool ThrowMarketNotFoundOnCreate { get; init; }

        public DepartmentDto? CurrentDepartment { get; set; } = new()
        {
            Id = 6,
            MarketId = 3,
            Name = "Produce",
            Description = "Fresh produce",
            IsActive = true
        };

        public Dictionary<(int MarketId, string Name), int> ExistingDepartmentNames { get; } = new();

        public Task<IReadOnlyCollection<DepartmentDto>> GetDepartmentsAsync(
            int? marketId = null,
            CancellationToken cancellationToken = default)
        {
            LastMarketId = marketId;
            return Task.FromResult(_departments);
        }

        public Task<DepartmentDto?> GetDepartmentAsync(
            int id,
            bool includeInactive = false,
            CancellationToken cancellationToken = default)
        {
            if (CurrentDepartment?.Id != id)
            {
                return Task.FromResult<DepartmentDto?>(null);
            }

            if (!includeInactive && !CurrentDepartment.IsActive)
            {
                return Task.FromResult<DepartmentDto?>(null);
            }

            return Task.FromResult<DepartmentDto?>(CurrentDepartment);
        }

        public Task<bool> MarketExistsAsync(
            int marketId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(MarketExists);
        }

        public Task<bool> DepartmentNameExistsAsync(
            int marketId,
            string name,
            int? excludedDepartmentId = null,
            CancellationToken cancellationToken = default)
        {
            var exists = ExistingDepartmentNames.TryGetValue((marketId, name.Trim()), out var ownerId) &&
                (!excludedDepartmentId.HasValue || ownerId != excludedDepartmentId.Value);

            return Task.FromResult(exists);
        }

        public Task<DepartmentDto> CreateDepartmentAsync(
            CreateDepartmentRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateDepartmentWasCalled = true;

            if (ThrowMarketNotFoundOnCreate)
            {
                throw new DepartmentMarketNotFoundException();
            }

            if (ExistingDepartmentNames.ContainsKey((request.MarketId, request.Name)))
            {
                throw new DepartmentNameConflictException();
            }

            return Task.FromResult(new DepartmentDto
            {
                Id = 6,
                MarketId = request.MarketId,
                Name = request.Name,
                Description = request.Description,
                IsActive = true
            });
        }

        public Task<DepartmentDto?> UpdateDepartmentAsync(
            int id,
            UpdateDepartmentRequest request,
            CancellationToken cancellationToken = default)
        {
            UpdateDepartmentWasCalled = true;

            if (CurrentDepartment is null || CurrentDepartment.Id != id)
            {
                return Task.FromResult<DepartmentDto?>(null);
            }

            CurrentDepartment.Name = request.Name;
            CurrentDepartment.Description = request.Description;

            return Task.FromResult<DepartmentDto?>(CurrentDepartment);
        }

        public Task<DepartmentDto?> SetDepartmentActiveStateAsync(
            int id,
            bool isActive,
            CancellationToken cancellationToken = default)
        {
            SetDepartmentActiveStateWasCalled = true;
            LastActiveState = isActive;

            if (CurrentDepartment is null || CurrentDepartment.Id != id)
            {
                return Task.FromResult<DepartmentDto?>(null);
            }

            CurrentDepartment.IsActive = isActive;
            return Task.FromResult<DepartmentDto?>(CurrentDepartment);
        }
    }
}
