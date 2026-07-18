using TripRadar.Server.Domain.ValueObjects;

namespace TripRadar.Server.Application.Contracts.Repositories.Models;

public sealed class ScheduledExecutionDetails
{
    public Guid ScheduledExecutionUniqueId { get; set; }

    public string ServiceType { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime NextExecutionTime { get; set; }

    public string Schedule { get; set; } = string.Empty;

    public DateTime CreatedOn { get; set; }

    public DateTime? UpdatedOn { get; set; }

    public string RequestSummary { get; set; } = string.Empty;

    public string? SearchQuery { get; set; }

    public string? Location { get; set; }

    public int? Radius { get; set; }

    public string? DepartureAirportCode { get; set; }

    public string? DepartureAirportCity { get; set; }

    public string? DestinationAirportCode { get; set; }

    public string? DestinationAirportCity { get; set; }

    public DateTime? DepartureDate { get; set; }

    public DateTime? ReturnDate { get; set; }

    public DateTime? CheckInDate { get; set; }

    public DateTime? CheckOutDate { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public string? AdditionalParameters { get; set; }

    public IList<QueryColumn>? SelectedColumns { get; set; }
}

