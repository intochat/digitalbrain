using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TripRadar.Server.Application.Constants.ScheduledExecutions;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Infrastructure.Database;
using TripRadar.Server.Infrastructure.Services;

namespace TripRadar.Server.Tests.Scheduling;

public class ScheduledExecutionDetailsQueryServiceTests
{
    [Fact]
    public async Task GetByUserIdAsync_HidesInvalidExecutions_AndDeactivatesThem()
    {
        var options = new DbContextOptionsBuilder<TripRadarDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new TripRadarDbContext(options);

        var invalidHotelExecution = new ScheduledExecution(
            userId: 42,
            name: ScheduledExecutionConstants.ScheduledHotel,
            nextExecutionTime: new DateTime(2026, 4, 20, 10, 0, 0, DateTimeKind.Utc),
            schedule: "0 * * * *");
        var invalidEventExecution = new ScheduledExecution(
            userId: 42,
            name: ScheduledExecutionConstants.ScheduledEvent,
            nextExecutionTime: new DateTime(2026, 4, 20, 10, 0, 0, DateTimeKind.Utc),
            schedule: "0 * * * *");
        var validLocalPlacesExecution = new ScheduledExecution(
            userId: 42,
            name: ScheduledExecutionConstants.ScheduledLocalPlaces,
            nextExecutionTime: new DateTime(2026, 4, 20, 10, 0, 0, DateTimeKind.Utc),
            schedule: "0 * * * *");

        dbContext.ScheduledExecutions.AddRange(invalidHotelExecution, invalidEventExecution, validLocalPlacesExecution);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        dbContext.ScheduledHotelQueries.Add(new ScheduledHotelQuery(
            location: "Sofia",
            scheduledExecutionId: invalidHotelExecution.Id,
            userId: 42,
            checkInDate: new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            checkOutDate: new DateTime(2026, 4, 24, 0, 0, 0, DateTimeKind.Utc)));

        dbContext.ScheduledEventQueries.Add(new ScheduledEventQuery(
            searchQuery: "concerts",
            scheduledExecutionId: invalidEventExecution.Id,
            userId: 42,
            additionalParameters: """{"location":"Rome","startDate":"2026-04-20","endDate":"2026-04-21"}"""));

        dbContext.ScheduledLocalPlacesQueries.Add(new ScheduledLocalPlaceQuery(
            searchQuery: "cafes",
            scheduledExecutionId: validLocalPlacesExecution.Id,
            userId: 42,
            additionalParameters: """{"location":"Rome","radius":1000}"""));

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var scheduledExecutionRepository = new Mock<IScheduledExecutionRepository>();
        var recurringJobService = new Mock<IRecurringJobService>();
        var service = new ScheduledExecutionDetailsQueryService(
            dbContext,
            Mock.Of<ILogger<ScheduledExecutionDetailsQueryService>>(),
            scheduledExecutionRepository.Object,
            recurringJobService.Object,
            new ScheduledExecutionValidityService());

        var result = await service.GetByUserIdAsync(42, TestContext.Current.CancellationToken);

        result.Should().HaveCount(1);
        result.Single().ScheduledExecutionUniqueId.Should().Be(validLocalPlacesExecution.UniqueId);
        scheduledExecutionRepository.Verify(x => x.UpdateActiveStatusAsync(invalidHotelExecution.UniqueId, false, It.IsAny<CancellationToken>()), Times.Once);
        scheduledExecutionRepository.Verify(x => x.UpdateActiveStatusAsync(invalidEventExecution.UniqueId, false, It.IsAny<CancellationToken>()), Times.Once);
        recurringJobService.Verify(x => x.DeleteRecurringExecution(invalidHotelExecution.UniqueId), Times.Once);
        recurringJobService.Verify(x => x.DeleteRecurringExecution(invalidEventExecution.UniqueId), Times.Once);
        recurringJobService.Verify(x => x.DeleteRecurringExecution(validLocalPlacesExecution.UniqueId), Times.Never);
    }

    [Fact]
    public async Task GetByUserIdAsync_ReturnsPausedExecution_WhenItIsStillValid()
    {
        var options = new DbContextOptionsBuilder<TripRadarDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new TripRadarDbContext(options);

        var pausedExecution = new ScheduledExecution(
            userId: 42,
            name: ScheduledExecutionConstants.ScheduledHotel,
            nextExecutionTime: DateTime.UtcNow.AddDays(1),
            schedule: "0 * * * *");
        pausedExecution.UpdateActiveStatus(false);

        dbContext.ScheduledExecutions.Add(pausedExecution);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        dbContext.ScheduledHotelQueries.Add(new ScheduledHotelQuery(
            location: "Sofia",
            scheduledExecutionId: pausedExecution.Id,
            userId: 42,
            checkInDate: DateTime.UtcNow.AddDays(7),
            checkOutDate: DateTime.UtcNow.AddDays(10)));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var scheduledExecutionRepository = new Mock<IScheduledExecutionRepository>();
        var recurringJobService = new Mock<IRecurringJobService>();
        var service = new ScheduledExecutionDetailsQueryService(
            dbContext,
            Mock.Of<ILogger<ScheduledExecutionDetailsQueryService>>(),
            scheduledExecutionRepository.Object,
            recurringJobService.Object,
            new ScheduledExecutionValidityService());

        var result = await service.GetByUserIdAsync(42, TestContext.Current.CancellationToken);

        result.Should().ContainSingle();
        result.Single().ScheduledExecutionUniqueId.Should().Be(pausedExecution.UniqueId);
        result.Single().IsActive.Should().BeFalse();
        scheduledExecutionRepository.Verify(x => x.UpdateActiveStatusAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        recurringJobService.Verify(x => x.DeleteRecurringExecution(It.IsAny<Guid>()), Times.Never);
    }
    [Fact]
    public async Task GetByUserIdAsync_HidesOrphanedFlightExecution_WithoutLinkedQuery()
    {
        var options = new DbContextOptionsBuilder<TripRadarDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new TripRadarDbContext(options);

        var orphanedFlightExecution = new ScheduledExecution(
            userId: 42,
            name: ScheduledExecutionConstants.ScheduledFlight,
            nextExecutionTime: DateTime.UtcNow.AddDays(1),
            schedule: "0 * * * *");

        dbContext.ScheduledExecutions.Add(orphanedFlightExecution);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var scheduledExecutionRepository = new Mock<IScheduledExecutionRepository>();
        var recurringJobService = new Mock<IRecurringJobService>();
        var service = new ScheduledExecutionDetailsQueryService(
            dbContext,
            Mock.Of<ILogger<ScheduledExecutionDetailsQueryService>>(),
            scheduledExecutionRepository.Object,
            recurringJobService.Object,
            new ScheduledExecutionValidityService());

        var result = await service.GetByUserIdAsync(42, TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
        scheduledExecutionRepository.Verify(x => x.UpdateActiveStatusAsync(orphanedFlightExecution.UniqueId, false, It.IsAny<CancellationToken>()), Times.Once);
        recurringJobService.Verify(x => x.DeleteRecurringExecution(orphanedFlightExecution.UniqueId), Times.Once);
    }
}


