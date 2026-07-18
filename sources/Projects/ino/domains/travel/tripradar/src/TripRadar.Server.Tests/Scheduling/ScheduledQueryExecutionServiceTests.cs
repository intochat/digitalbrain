using System.Reflection;
using Microsoft.Extensions.Logging;
using Moq;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Infrastructure.Contracts.Scheduled;
using TripRadar.Server.Infrastructure.Services;
using TripRadar.Server.Infrastructure.Services.Strategies;

namespace TripRadar.Server.Tests.Scheduling;

public class ScheduledQueryExecutionServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IScheduledExecutionRepository> _scheduledExecutionRepository = new();
    private readonly Mock<IServiceTokenCostRepository> _serviceTokenCostRepository = new();
    private readonly Mock<IUserLimitService> _userLimitService = new();
    private readonly Mock<IUsageEventWriter> _usageEventWriter = new();
    private readonly Mock<IScheduledJobManager> _scheduledJobManager = new();
    private readonly Mock<ICronExpressionService> _cronExpressionService = new();
    private readonly Mock<IScheduledExecutionStrategy> _strategy = new();
    private readonly Mock<IUserRepository> _userRepository = new();

    [Fact]
    public async Task ExecuteQueryAsync_DeactivatesInvalidHotelRequest_WithoutExecutingStrategy()
    {
        var user = User.CreateFromTelegramAuth(777, "tester", null, null, null);
        SetPrivateProperty(user, nameof(User.Id), 42L);

        var scheduledExecution = new ScheduledExecution(
            userId: 42,
            name: "scheduled-hotel",
            nextExecutionTime: new DateTime(2026, 4, 20, 10, 0, 0, DateTimeKind.Utc),
            schedule: "0 * * * *");
        SetPrivateProperty(scheduledExecution, nameof(ScheduledExecution.Id), 101L);

        var scheduledHotelQuery = new ScheduledHotelQuery(
            location: "Sofia",
            scheduledExecutionId: 101,
            userId: 42,
            checkInDate: new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            checkOutDate: new DateTime(2026, 4, 22, 0, 0, 0, DateTimeKind.Utc));

        _scheduledExecutionRepository
            .Setup(x => x.GetByUniqueIdAsync(scheduledExecution.UniqueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scheduledExecution);
        _scheduledExecutionRepository
            .Setup(x => x.UpdateActiveStatusAsync(scheduledExecution.UniqueId, false, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _userRepository
            .Setup(x => x.GetByIdForLimitsAsync(42L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var hotelRepository = new Mock<IScheduledHotelQueryRepository>();
        hotelRepository
            .Setup(x => x.GetByScheduledExecutionIdAsync(101L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scheduledHotelQuery);

        _unitOfWork.SetupGet(x => x.UserRepository).Returns(_userRepository.Object);
        _unitOfWork.SetupGet(x => x.ScheduledHotelQueryRepository).Returns(hotelRepository.Object);
        _unitOfWork
            .Setup(x => x.StartScopeAsync(It.IsAny<System.Transactions.TransactionScopeOption>(), It.IsAny<System.Transactions.IsolationLevel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UnitOfWorkTransactionScope.Noop());

        _userLimitService
            .Setup(x => x.PrepareTokenConsumptionAsync(user, ServiceType.Hotel, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new TokenConsumptionTicket("tester", ServiceType.Hotel, TokenConsumptionType.Tier)));
        _userLimitService
            .Setup(x => x.RollbackTokenConsumptionAsync(user, It.IsAny<TokenConsumptionTicket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _strategy.Setup(x => x.CanHandle(ScheduledExecutionSearchType.Hotels)).Returns(true);

        var service = new ScheduledQueryExecutionService(
            _unitOfWork.Object,
            _scheduledExecutionRepository.Object,
            _serviceTokenCostRepository.Object,
            Mock.Of<ILogger<ScheduledQueryExecutionService>>(),
            _userLimitService.Object,
            _usageEventWriter.Object,
            _scheduledJobManager.Object,
            _cronExpressionService.Object,
            new ScheduledExecutionValidityService(),
            [_strategy.Object]);

        await service.ExecuteQueryAsync(scheduledExecution.UniqueId, TestContext.Current.CancellationToken);

        _scheduledExecutionRepository.Verify(x => x.UpdateActiveStatusAsync(scheduledExecution.UniqueId, false, It.IsAny<CancellationToken>()), Times.Once);
        _scheduledJobManager.Verify(x => x.RemoveIfExists($"scheduled-execution-{scheduledExecution.UniqueId}"), Times.Once);
        _strategy.Verify(x => x.ExecuteAsync(It.IsAny<ScheduledExecution>(), It.IsAny<CancellationToken>()), Times.Never);
        _userLimitService.Verify(x => x.RollbackTokenConsumptionAsync(user, It.IsAny<TokenConsumptionTicket>(), It.IsAny<CancellationToken>()), Times.Once);
        _usageEventWriter.Verify(x => x.WriteAsync(It.IsAny<long>(), It.IsAny<ServiceType>(), It.IsAny<decimal>(), It.IsAny<UsageEventSourceType>(), It.IsAny<DateTime>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteQueryAsync_DeactivatesOrphanedFlightRequest_WithoutExecutingStrategy()
    {
        var user = User.CreateFromTelegramAuth(777, "tester", null, null, null);
        SetPrivateProperty(user, nameof(User.Id), 42L);

        var scheduledExecution = new ScheduledExecution(
            userId: 42,
            name: "scheduled-flight",
            nextExecutionTime: DateTime.UtcNow.AddDays(1),
            schedule: "0 * * * *");
        SetPrivateProperty(scheduledExecution, nameof(ScheduledExecution.Id), 202L);

        _scheduledExecutionRepository
            .Setup(x => x.GetByUniqueIdAsync(scheduledExecution.UniqueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scheduledExecution);
        _scheduledExecutionRepository
            .Setup(x => x.UpdateActiveStatusAsync(scheduledExecution.UniqueId, false, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _userRepository
            .Setup(x => x.GetByIdForLimitsAsync(42L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var flightRepository = new Mock<IScheduledFlightQueryRepository>();
        flightRepository
            .Setup(x => x.GetByScheduledExecutionIdAsync(202L, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledFlightQuery?)null);

        _unitOfWork.SetupGet(x => x.UserRepository).Returns(_userRepository.Object);
        _unitOfWork.SetupGet(x => x.ScheduledFlightQueryRepository).Returns(flightRepository.Object);
        _unitOfWork
            .Setup(x => x.StartScopeAsync(It.IsAny<System.Transactions.TransactionScopeOption>(), It.IsAny<System.Transactions.IsolationLevel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UnitOfWorkTransactionScope.Noop());

        _userLimitService
            .Setup(x => x.PrepareTokenConsumptionAsync(user, ServiceType.Flight, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new TokenConsumptionTicket("tester", ServiceType.Flight, TokenConsumptionType.Tier)));
        _userLimitService
            .Setup(x => x.RollbackTokenConsumptionAsync(user, It.IsAny<TokenConsumptionTicket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _strategy.Setup(x => x.CanHandle(ScheduledExecutionSearchType.Flights)).Returns(true);

        var service = new ScheduledQueryExecutionService(
            _unitOfWork.Object,
            _scheduledExecutionRepository.Object,
            _serviceTokenCostRepository.Object,
            Mock.Of<ILogger<ScheduledQueryExecutionService>>(),
            _userLimitService.Object,
            _usageEventWriter.Object,
            _scheduledJobManager.Object,
            _cronExpressionService.Object,
            new ScheduledExecutionValidityService(),
            [_strategy.Object]);

        await service.ExecuteQueryAsync(scheduledExecution.UniqueId, TestContext.Current.CancellationToken);

        _scheduledExecutionRepository.Verify(x => x.UpdateActiveStatusAsync(scheduledExecution.UniqueId, false, It.IsAny<CancellationToken>()), Times.Once);
        _scheduledJobManager.Verify(x => x.RemoveIfExists($"scheduled-execution-{scheduledExecution.UniqueId}"), Times.Once);
        _strategy.Verify(x => x.ExecuteAsync(It.IsAny<ScheduledExecution>(), It.IsAny<CancellationToken>()), Times.Never);
        _userLimitService.Verify(x => x.RollbackTokenConsumptionAsync(user, It.IsAny<TokenConsumptionTicket>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static void SetPrivateProperty(object target, string propertyName, object value)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var property = target.GetType()
            .GetProperties(flags)
            .FirstOrDefault(p => p.Name == propertyName && p.DeclaringType == target.GetType())
            ?? target.GetType().GetProperties(flags).FirstOrDefault(p => p.Name == propertyName);

        property.Should().NotBeNull();
        property!.SetValue(target, value);
    }
}
