using System.Reflection;
using Moq;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Constants.ScheduledExecutions;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.UseCases.ScheduledExecutions.Commands.UpdateScheduledExecutionConfiguration;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Infrastructure.Services;

namespace TripRadar.Server.Tests.Scheduling;

public class UpdateScheduledExecutionConfigurationCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IScheduledExecutionRepository> _scheduledExecutionRepository = new();
    private readonly Mock<IRecurringJobService> _recurringJobService = new();
    private readonly Mock<ICurrentUserContext> _currentUserContext = new();
    private readonly Mock<IScheduledFlightQueryRepository> _scheduledFlightQueryRepository = new();
    private readonly UpdateScheduledExecutionConfigurationCommandHandler _handler;

    public UpdateScheduledExecutionConfigurationCommandHandlerTests()
    {
        _unitOfWork
            .Setup(x => x.StartScopeAsync(It.IsAny<System.Transactions.TransactionScopeOption>(), It.IsAny<System.Transactions.IsolationLevel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UnitOfWorkTransactionScope.Noop());
        _unitOfWork.SetupGet(x => x.ScheduledFlightQueryRepository).Returns(_scheduledFlightQueryRepository.Object);

        _handler = new UpdateScheduledExecutionConfigurationCommandHandler(
            _unitOfWork.Object,
            _scheduledExecutionRepository.Object,
            _recurringJobService.Object,
            _currentUserContext.Object,
            new ScheduledExecutionValidityService());
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenNextRunIsAfterFlightDepartureDate()
    {
        var user = User.CreateFromTelegramAuth(777, "tester", null, null, null);
        SetPrivateProperty(user, nameof(User.Id), 42L);
        _currentUserContext.Setup(x => x.GetRequiredUser()).Returns(user);

        var scheduledExecution = new ScheduledExecution(
            userId: 42,
            name: ScheduledExecutionConstants.ScheduledFlight,
            nextExecutionTime: new DateTime(2026, 4, 18, 12, 0, 0, DateTimeKind.Utc),
            schedule: "0 * * * *");
        SetPrivateProperty(scheduledExecution, nameof(ScheduledExecution.Id), 100L);

        _scheduledExecutionRepository
            .Setup(x => x.GetByUniqueIdAsync(scheduledExecution.UniqueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scheduledExecution);

        _scheduledFlightQueryRepository
            .Setup(x => x.GetByScheduledExecutionIdAsync(100L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduledFlightQuery(
                departureAirportId: 1,
                destinationAirportId: 2,
                scheduledExecutionId: 100,
                userId: 42,
                departureDate: new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc),
                returnDate: null));

        var command = new UpdateScheduledExecutionConfigurationCommand(
            scheduledExecution.UniqueId,
            "tester",
            IsActive: true,
            Schedule: "0 * * * *",
            NextExecutionTime: new DateTime(2026, 4, 18, 16, 0, 0, DateTimeKind.Utc));

        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Errors.InvalidScheduledExecutionWindow);
        _scheduledExecutionRepository.Verify(
            x => x.UpdateConfigurationAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _recurringJobService.Verify(x => x.ScheduleRecurringExecution(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
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
