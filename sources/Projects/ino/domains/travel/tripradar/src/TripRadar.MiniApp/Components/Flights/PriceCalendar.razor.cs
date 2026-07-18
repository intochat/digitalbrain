using Microsoft.AspNetCore.Components;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Components.Flights;

public partial class PriceCalendar
{
    [Parameter] public string? DepartureId { get; set; }
    [Parameter] public string? ArrivalId { get; set; }
    [Parameter] public bool Visible { get; set; }
    [Parameter] public bool CanLoadPrices { get; set; }
    [Parameter] public bool ShowUpgradeBanner { get; set; }
    [Parameter] public int? TripLengthDays { get; set; }
    [Parameter] public string? MinDate { get; set; }
    [Parameter] public EventCallback<string> OnDateSelected { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private bool _wasVisible;
    private DateOnly _displayedDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private string? _selectedDate;
    private string? _lastAnchorDate;
    private string? _lastRouteKey;

    // accumulated prices across loaded months
    private readonly Dictionary<string, PriceCalendarDay> _loadedPrices = new();
    private readonly HashSet<string> _loadedMonths = [];
    private readonly HashSet<string> _loadingMonths = [];
    private readonly HashSet<string> _loadingDates = [];
    private bool _hasLoadedAny;
    
    private readonly string[] _dayHeaders = ["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"];
    private int _leadingBlanks;
    private List<CalendarCell> _cells = [];

    private string Subtitle
    {
        get
        {
            if (_loadingDates.Count > 0) return L["PriceCalLoadingPrices"];
            if (_hasLoadedAny) return L["PriceCalPricesLoaded"];
            return L["PriceCalSubtitle"];
        }
    }

    private string HintText => _hasLoadedAny ? L["PriceCalCompareHint"] : L["PriceCalTapHint"];

    private string ApplyButtonText
    {
        get
        {
            if (string.IsNullOrEmpty(_selectedDate)) return L["PriceCalApply"];
            if (!DateOnly.TryParseExact(_selectedDate, "yyyy-MM-dd", out var date)) return L["PriceCalApply"];
            return string.Format(L["PriceCalApplyWithDate"], date.ToString("ddd, MMM d"));
        }
    }

    private bool CanGoPrevious
    {
        get
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var minDate = DateOnly.TryParseExact(MinDate, "yyyy-MM-dd", out var parsed) ? parsed : (DateOnly?)null;
            var earliest = minDate.HasValue && minDate.Value > today ? minDate.Value : today;
            return _displayedDate.Year > earliest.Year || (_displayedDate.Year == earliest.Year && _displayedDate.Month > earliest.Month);
        }
    }

    private bool CanGoNext
    {
        get
        {
            var maxDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(11);
            return _displayedDate.Year < maxDate.Year || (_displayedDate.Year == maxDate.Year && _displayedDate.Month <= maxDate.Month);
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (Visible)
        {
            var routeKey = $"{DepartureId}|{ArrivalId}|{MinDate}|{CanLoadPrices}|{TripLengthDays}";
            if (!_wasVisible || _lastRouteKey != routeKey)
            {
                ResetState();
                _lastRouteKey = routeKey;
            }

            BuildCells();
            await LoadDisplayedMonthAsync();
        }

        _wasVisible = Visible;
    }

    private void ResetState()
    {
        _loadedPrices.Clear();
        _loadedMonths.Clear();
        _loadingMonths.Clear();
        _loadingDates.Clear();
        _hasLoadedAny = false;
        _selectedDate = null;
        _lastAnchorDate = null;

        _displayedDate = DateOnly.TryParseExact(MinDate, "yyyy-MM-dd", out var min)
            ? new DateOnly(min.Year, min.Month, 1)
            : DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private void BuildCells()
    {
        var daysInMonth = DateTime.DaysInMonth(_displayedDate.Year, _displayedDate.Month);
        var firstDay = new DateOnly(_displayedDate.Year, _displayedDate.Month, 1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var minDate = DateOnly.TryParseExact(MinDate, "yyyy-MM-dd", out var parsed) ? parsed : (DateOnly?)null;
        var earliest = minDate.HasValue && minDate.Value > today ? minDate.Value : today;

        _leadingBlanks = ((int)firstDay.DayOfWeek + 6) % 7;

        _cells = new List<CalendarCell>(daysInMonth);
        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(_displayedDate.Year, _displayedDate.Month, day);
            _cells.Add(new CalendarCell
            {
                Day = day,
                DateString = date.ToString("yyyy-MM-dd"),
                IsPast = date < earliest
            });
        }
    }


    private async Task OnDateClick(string dateString)
    {
        _selectedDate = dateString;

        if (CanLoadPrices)
        {
            await LoadDisplayedMonthAsync();
        }

        StateHasChanged();
    }

    private async Task PreviousMonth()
    {
        _displayedDate = _displayedDate.AddMonths(-1);
        BuildCells();
        await LoadDisplayedMonthAsync();
    }

    private async Task NextMonth()
    {
        _displayedDate = _displayedDate.AddMonths(1);
        BuildCells();
        await LoadDisplayedMonthAsync();
    }

    private async Task LoadDisplayedMonthAsync()
    {
        if (!CanLoadPrices
            || string.IsNullOrWhiteSpace(DepartureId)
            || string.IsNullOrWhiteSpace(ArrivalId))
        {
            return;
        }

        var monthKey = MonthKey(_displayedDate);
        if (_loadedMonths.Contains(monthKey) || _loadingMonths.Contains(monthKey))
        {
            return;
        }

        var datesToMarkAsLoading = _cells
            .Where(cell => !cell.IsPast && !_loadedPrices.ContainsKey(cell.DateString))
            .Select(cell => cell.DateString)
            .ToList();

        _loadingMonths.Add(monthKey);
        _loadingDates.UnionWith(datesToMarkAsLoading);
        StateHasChanged();

        try
        {
            var loadedCountBefore = _loadedPrices.Count;
            var result = await CalendarService.GetPriceCalendarAsync(DepartureId, ArrivalId, _displayedDate.Year, _displayedDate.Month, TripLengthDays);
            if (result?.Days is not null)
            {
                foreach (var day in result.Days)
                {
                    if (day.LowestPrice is > 0)
                    {
                        _loadedPrices[day.Date] = day;
                    }
                }
            }

            _loadedMonths.Add(monthKey);
            _hasLoadedAny = _loadedPrices.Count > loadedCountBefore || _loadedPrices.Count > 0;
            if (result?.Days != null)
            {
                _lastAnchorDate = result.CheapestDate ??
                                  result.Days.FirstOrDefault(day => day.LowestPrice is > 0)?.Date;
            }
        }
        catch
        {
            // Keep the month retryable on the next open/navigation attempt.
        }
        finally
        {
            _loadingMonths.Remove(monthKey);
            foreach (var date in datesToMarkAsLoading)
            {
                _loadingDates.Remove(date);
            }

            StateHasChanged();
        }
    }

    private static string MonthKey(DateOnly date) => $"{date.Year:D4}-{date.Month:D2}";

    private async Task ApplySelectedDate()
    {
        if (!string.IsNullOrEmpty(_selectedDate))
        {
            await OnDateSelected.InvokeAsync(_selectedDate);
            await OnClose.InvokeAsync();
        }
    }

    private async Task CloseOverlay() => await OnClose.InvokeAsync();

    private int AnimationDelay(string dateString)
    {
        if (_lastAnchorDate is null) return 0;
        var d1 = DateOnly.ParseExact(dateString, "yyyy-MM-dd");
        var d2 = DateOnly.ParseExact(_lastAnchorDate, "yyyy-MM-dd");
        return Math.Abs((d1.ToDateTime(TimeOnly.MinValue) - d2.ToDateTime(TimeOnly.MinValue)).Days) * 60;
    }

    private PriceBand GetPriceBand(string dateString)
    {
        if (!_loadedPrices.TryGetValue(dateString, out var day) || day.LowestPrice is not > 0)
        {
            return PriceBand.None;
        }

        var prices = _loadedPrices.Values
            .Select(item => item.LowestPrice)
            .Where(price => price is > 0)
            .Select(price => price!.Value)
            .OrderBy(price => price)
            .ToList();

        if (prices.Count < 3)
        {
            return PriceBand.Standard;
        }

        var cheapLimit = prices[(prices.Count - 1) / 3];
        var expensiveLimit = prices[((prices.Count - 1) * 2) / 3];
        if (cheapLimit >= expensiveLimit)
        {
            return PriceBand.Standard;
        }

        if (day.LowestPrice.Value <= cheapLimit) return PriceBand.Cheap;
        if (day.LowestPrice.Value >= expensiveLimit) return PriceBand.Expensive;
        return PriceBand.Standard;
    }

    private static string CellClass(bool isPast, bool isSelected, PriceBand priceBand, bool isLoading = false)
    {
        var b = "flex flex-col items-center justify-center py-1.5 rounded-xl transition-all min-h-[52px] border focus:outline-none";
        if (isLoading) return $"{b} price-loading-cell cursor-default";
        if (isPast) return $"{b} border-transparent cursor-default opacity-40";

        var bandClass = PriceBandCellClass(priceBand);
        if (isSelected) return $"{b} {bandClass} ring-2 ring-blue-500 dark:ring-blue-400 shadow-md shadow-blue-200/60 dark:shadow-blue-900/50";
        if (priceBand != PriceBand.None) return $"{b} {bandClass} cursor-pointer";
        return $"{b} border-transparent cursor-pointer hover:bg-gray-50 dark:hover:bg-slate-800";
    }

    private static string PriceBandCellClass(PriceBand priceBand) => priceBand switch
    {
        PriceBand.Cheap => "bg-emerald-50 border-emerald-200 hover:bg-emerald-100 dark:bg-emerald-500/15 dark:border-emerald-500/30 dark:hover:bg-emerald-500/20",
        PriceBand.Standard => "bg-orange-50 border-orange-200 hover:bg-orange-100 dark:bg-orange-500/15 dark:border-orange-500/30 dark:hover:bg-orange-500/20",
        PriceBand.Expensive => "bg-red-50 border-red-200 hover:bg-red-100 dark:bg-red-500/15 dark:border-red-500/30 dark:hover:bg-red-500/20",
        _ => "border-transparent"
    };

    private static string DayTextClass(bool isPast, bool isSelected, PriceBand priceBand, bool isLoading = false)
    {
        if (isLoading) return "text-sm font-semibold text-blue-500 dark:text-blue-400";
        if (isPast) return "text-sm text-gray-300 dark:text-slate-600";
        if (isSelected) return "text-sm font-bold text-gray-950 dark:text-white";

        return priceBand switch
        {
            PriceBand.Cheap => "text-sm font-semibold text-emerald-900 dark:text-emerald-100",
            PriceBand.Standard => "text-sm font-semibold text-orange-900 dark:text-orange-100",
            PriceBand.Expensive => "text-sm font-semibold text-red-900 dark:text-red-100",
            _ => "text-sm font-medium text-gray-800 dark:text-slate-200"
        };
    }

    private static string PriceTextClass(bool isSelected, PriceBand priceBand)
    {
        var selectedWeight = isSelected ? "font-bold" : "font-semibold";
        return priceBand switch
        {
            PriceBand.Cheap => $"text-[10px] {selectedWeight} text-emerald-700 dark:text-emerald-300 mt-0.5 animate-price-reveal",
            PriceBand.Standard => $"text-[10px] {selectedWeight} text-orange-700 dark:text-orange-300 mt-0.5 animate-price-reveal",
            PriceBand.Expensive => $"text-[10px] {selectedWeight} text-red-700 dark:text-red-300 mt-0.5 animate-price-reveal",
            _ => $"text-[10px] {selectedWeight} text-gray-500 dark:text-slate-400 mt-0.5 animate-price-reveal"
        };
    }

    private enum PriceBand
    {
        None,
        Cheap,
        Standard,
        Expensive
    }
    private sealed class CalendarCell
    {
        public int Day { get; init; }
        public string DateString { get; init; } = "";
        public bool IsPast { get; init; }
    }
}
