using Moq;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.UseCases.SearchEngine.Events.Commands.CreateScheduledEventQuery;
using TripRadar.Server.Application.UseCases.SearchEngine.Flights.Commands.CreateScheduledFlightQuery;
using TripRadar.Server.Application.UseCases.SearchEngine.Hotels.Commands.CreateScheduledHotelQuery;
using TripRadar.Server.Infrastructure.Services;

namespace TripRadar.Server.Tests.Scheduling;

public class CreateScheduledQueryValidatorsTests
{
    private readonly ScheduledExecutionValidityService _validityService = new();

    [Fact]
    public async Task FlightValidator_Fails_WhenNextRunIsAfterDepartureDate()
    {
        var referenceLookupValidator = new Mock<IReferenceLookupValidator>();
        var validator = new CreateScheduledFlightQueryCommandValidator(referenceLookupValidator.Object, _validityService);
        var command = new CreateScheduledFlightQueryCommand(
            DepartureAirportCode: "JFK",
            DestinationAirportCode: "LHR",
            Username: "tester",
            DepartureDate: new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            ReturnDate: null,
            SelectedColumns: [],
            AdditionalParametersJson: "{}",
            NextExecutionTime: new DateTime(2026, 4, 20, 8, 0, 0, DateTimeKind.Utc),
            Schedule: "0 * * * *");

        var result = await validator.ValidateAsync(command, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.ErrorMessage.Contains("Next execution time"));
    }

    [Fact]
    public async Task HotelValidator_Fails_WhenNextRunIsAfterCheckInDate()
    {
        var validator = new CreateScheduledHotelQueryCommandValidator(_validityService);
        var command = new CreateScheduledHotelQueryCommand(
            Location: "Sofia",
            Username: "tester",
            CheckInDate: new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            CheckOutDate: new DateTime(2026, 4, 24, 0, 0, 0, DateTimeKind.Utc),
            SelectedColumns: [],
            AdditionalParametersJson: "{}",
            NextExecutionTime: new DateTime(2026, 4, 20, 6, 0, 0, DateTimeKind.Utc),
            Schedule: "0 * * * *");

        var result = await validator.ValidateAsync(command, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.ErrorMessage.Contains("Next execution time"));
    }

    [Fact]
    public async Task EventValidator_Fails_WhenNextRunIsAfterStartDate()
    {
        var validator = new CreateScheduledEventQueryCommandValidator(_validityService);
        var command = new CreateScheduledEventQueryCommand(
            Username: "tester",
            SearchQuery: "concerts",
            SelectedColumns: [],
            AdditionalParametersJson: """{"startDate":"2026-04-20","endDate":"2026-04-21","location":"Rome"}""",
            NextExecutionTime: new DateTime(2026, 4, 20, 9, 0, 0, DateTimeKind.Utc),
            Schedule: "0 * * * *");

        var result = await validator.ValidateAsync(command, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.ErrorMessage.Contains("Next execution time"));
    }
}
