using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Common;

public sealed record ScheduledExecution(
    [property: JsonPropertyName("scheduledExecutionUniqueId")] Guid UniqueId,
    [property: JsonPropertyName("serviceType")] string ServiceType,
    [property: JsonPropertyName("isActive")] bool IsActive,
    [property: JsonPropertyName("nextExecutionTime")] DateTime NextExecutionTime,
    [property: JsonPropertyName("schedule")] string Schedule,
    [property: JsonPropertyName("createdOn")] DateTime CreatedOn,
    [property: JsonPropertyName("updatedOn")] DateTime? UpdatedOn,
    [property: JsonPropertyName("requestSummary")] string RequestSummary,
    [property: JsonPropertyName("departureAirportCode")] string? DepartureAirportCode,
    [property: JsonPropertyName("departureAirportCity")] string? DepartureAirportCity,
    [property: JsonPropertyName("destinationAirportCode")] string? DestinationAirportCode,
    [property: JsonPropertyName("destinationAirportCity")] string? DestinationAirportCity,
    [property: JsonPropertyName("departureDate")] DateTime? DepartureDate,
    [property: JsonPropertyName("returnDate")] DateTime? ReturnDate,
    [property: JsonPropertyName("location")] string? Location,
    [property: JsonPropertyName("checkInDate")] DateTime? CheckInDate,
    [property: JsonPropertyName("checkOutDate")] DateTime? CheckOutDate
);