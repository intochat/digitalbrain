using TripRadar.MiniApp.Client.Infrastructure.Models.Common;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Pages;

public partial class Alerts
{
    private bool _loading = true;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        Tracking.OnChanged += StateHasChanged;
        await Load();
    }

    private async Task Load()
    {
        if (!Auth.IsAuthenticated)
        {
            _loading = false;
            return;
        }

        _loading = true;
        _error = null;
        StateHasChanged();

        try
        {
            await Tracking.LoadAsync();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task Toggle(ScheduledExecution tracking)
    {
        try
        {
            await Tracking.ToggleAsync(tracking.UniqueId);
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private async Task Delete(Guid uniqueId)
    {
        try
        {
            await Tracking.DeleteAsync(uniqueId);
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private void ViewPrices(ScheduledExecution t)
    {
        SearchState.Params = new()
        {
            DepartureId = t.DepartureAirportCode ?? "",
            ArrivalId = t.DestinationAirportCode ?? "",
            DepartureName = t.DepartureAirportCity ?? t.DepartureAirportCode ?? "",
            ArrivalName = t.DestinationAirportCity ?? t.DestinationAirportCode ?? "",
            OutboundDate = t.DepartureDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "",
            ReturnDate = t.ReturnDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "",
            Type = t.ReturnDate.HasValue ? FlightType.RoundTrip : FlightType.OneWay
        };
        Nav.NavigateTo(AppRoutes.FlightResults);
    }

    private static string FormatRoute(ScheduledExecution t) =>
        $"{t.DepartureAirportCity ?? t.DepartureAirportCode} → {t.DestinationAirportCity ?? t.DestinationAirportCode}";

    private static string FormatDate(ScheduledExecution t)
    {
        var date = t.DepartureDate?.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        if (t.ReturnDate.HasValue)
            return $"{date} – {t.ReturnDate.Value.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture)} · Round trip";
        return $"{date} · One way";
    }

    public void Dispose() => Tracking.OnChanged -= StateHasChanged;
}
