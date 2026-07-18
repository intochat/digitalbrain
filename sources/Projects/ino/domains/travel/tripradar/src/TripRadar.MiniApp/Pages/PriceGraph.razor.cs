using TripRadar.MiniApp.Components.Shared;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Pages;

public partial class PriceGraph
{
    private string _activeTab = "graph";
    private FlightPriceHistoryPoint? _selectedHistoryPoint;

    private bool IsLowPrice =>
        string.Equals(SearchState.Results?.PriceInsights?.PriceLevel, "low", StringComparison.OrdinalIgnoreCase);

    private string DepartureMonth
    {
        get
        {
            if (DateTime.TryParse(SearchState.Params.OutboundDate, out var date))
                return date.ToString("MMMM");
            return "";
        }
    }

    private int CheaperThanPercent
    {
        get
        {
            var insights = SearchState.Results?.PriceInsights;
            if (insights?.LowestPrice is null || insights.TypicalPriceRange is not { Length: 2 } range)
                return 0;

            var median = (range[0] + range[1]) / 2;
            if (median <= 0 || insights.LowestPrice.Value >= median)
                return 0;

            return (int)((1 - insights.LowestPrice.Value / median) * 100);
        }
    }

    private void OnHistoryPointClicked(FlightPriceHistoryPoint point)
    {
        _selectedHistoryPoint = _selectedHistoryPoint?.Date == point.Date ? null : point;
    }

    private string TabClass(string tab) => Css.Join(
        "flex-1 py-2 text-sm font-medium rounded-md text-center transition-colors",
        _activeTab == tab
            ? "bg-white dark:bg-slate-700 text-slate-900 dark:text-white shadow-sm"
            : "text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-300"
    );

    private static string FormatShortDate(string dateStr)
    {
        if (DateTime.TryParseExact(dateStr, "dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
            return date.ToString("dd/MM");

        return dateStr.Length > 5 ? dateStr[..5] : dateStr;
    }

    protected override async Task OnInitializedAsync()
    {
        SearchState.OnChanged += StateHasChanged;
        await UserPrefs.LoadAsync();
    }

    public void Dispose() => SearchState.OnChanged -= StateHasChanged;
}
