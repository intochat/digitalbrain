using TripRadar.MiniApp.Client.Infrastructure.Contracts;
using TripRadar.MiniApp.Client.Infrastructure.Models.Common;

namespace TripRadar.MiniApp.Client.Infrastructure.Services.State;

public sealed class TrackingState(IPriceTrackingManager api)
{
    private List<ScheduledExecution> _trackings = [];

    public IReadOnlyList<ScheduledExecution> Trackings => _trackings;
    public bool IsLoaded { get; private set; }

    public event Action? OnChanged;

    public async Task LoadAsync()
    {
        _trackings = await api.GetAllAsync();
        IsLoaded = true;
        OnChanged?.Invoke();
    }

    public bool IsTracking(string? departureCode, string? arrivalCode, string? date)
    {
        var match = FindTracking(departureCode, arrivalCode, date);
        return match is { IsActive: true };
    }

    public ScheduledExecution? FindTracking(string? departureCode, string? arrivalCode, string? date)
    {
        if (departureCode is null || arrivalCode is null || date is null)
            return null;

        var parsedDate = DateTime.TryParse(date, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d.Date : (DateTime?)null;

        if (parsedDate is null) return null;

        return _trackings.FirstOrDefault(t =>
            t.ServiceType == "Flights" &&
            string.Equals(t.DepartureAirportCode, departureCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t.DestinationAirportCode, arrivalCode, StringComparison.OrdinalIgnoreCase) &&
            t.DepartureDate.HasValue &&
            t.DepartureDate.Value.Date == parsedDate.Value);
    }

    public async Task ToggleAsync(Guid uniqueId)
    {
        var tracking = _trackings.FirstOrDefault(t => t.UniqueId == uniqueId);
        if (tracking is null) return;

        var newActive = !tracking.IsActive;
        await api.ToggleAsync(uniqueId, newActive, tracking.Schedule, tracking.NextExecutionTime);
        await LoadAsync();
    }

    public async Task<Guid?> CreateFlightAsync(string departureCode, string destinationCode, DateTime departureDate, DateTime? returnDate)
    {
        var existing = FindTracking(departureCode, destinationCode, departureDate.ToString("yyyy-MM-dd"));
        if (existing is not null)
        {
            if (!existing.IsActive)
                await ToggleAsync(existing.UniqueId);
            return existing.UniqueId;
        }

        var request = new CreateFlightTrackingRequest(
            DepartureAirportCode: departureCode,
            DestinationAirportCode: destinationCode,
            DepartureDate: departureDate,
            ReturnDate: returnDate,
            Schedule: "0 */6 * * *",
            NextExecutionTime: DateTime.UtcNow.AddHours(6));

        var response = await api.TrackFlightAsync(request);
        if (response is not null)
        {
            await LoadAsync();
            return response.UniqueId;
        }
        return null;
    }

    public async Task DeleteAsync(Guid uniqueId)
    {
        await api.DeleteAsync(uniqueId);
        _trackings.RemoveAll(t => t.UniqueId == uniqueId);
        OnChanged?.Invoke();
    }

    public int ActiveCount => _trackings.Count(t => t.IsActive);
}