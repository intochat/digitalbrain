using TripRadar.Server.Application.Contracts.Repositories.Models;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Infrastructure.Services;

namespace TripRadar.Server.Tests.Scheduling;

public class ScheduledExecutionValidityServiceTests
{
    private readonly ScheduledExecutionValidityService _service = new();

    [Fact]
    public void IsExecutableAtNextRun_ReturnsFalse_WhenHotelCheckInIsBeforeNextRun()
    {
        var result = _service.IsExecutableAtNextRun(
            ScheduledExecutionSearchType.Hotels,
            new DateTime(2026, 4, 20, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc));

        result.Should().BeFalse();
    }

    [Fact]
    public void IsExecutableAtNextRun_ReturnsFalse_WhenNextExecutionTimeIsAlreadyInPast_ForFlights()
    {
        var result = _service.IsExecutableAtNextRun(
            ScheduledExecutionSearchType.Flights,
            DateTime.UtcNow.AddMinutes(-5),
            DateTime.UtcNow.AddDays(2));

        result.Should().BeFalse();
    }

    [Fact]
    public void IsExecutableAtNextRun_ReturnsTrue_ForLocalPlacesWithoutStartBoundary()
    {
        var result = _service.IsExecutableAtNextRun(
            ScheduledExecutionSearchType.LocalPlaces,
            new DateTime(2026, 4, 20, 12, 0, 0, DateTimeKind.Utc),
            null);

        result.Should().BeTrue();
    }

    [Fact]
    public void ExtractEventDates_ReturnsUtcDates_FromAdditionalParameters()
    {
        const string additionalParameters = """{"startDate":"2026-04-20","endDate":"2026-04-22T14:00:00Z"}""";

        var startDate = _service.ExtractEventStartDate(additionalParameters);
        var endDate = _service.ExtractEventEndDate(additionalParameters);

        startDate.Should().Be(new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc));
        endDate.Should().Be(new DateTime(2026, 4, 22, 14, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void IsExecutableAtNextRun_UsesProjectedStartDate_ForEventDetails()
    {
        var details = new ScheduledExecutionDetails
        {
            ServiceType = ScheduledExecutionSearchType.Events.Name,
            NextExecutionTime = new DateTime(2026, 4, 21, 10, 0, 0, DateTimeKind.Utc),
            StartDate = new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc)
        };

        var result = _service.IsExecutableAtNextRun(details);

        result.Should().BeFalse();
    }
}
