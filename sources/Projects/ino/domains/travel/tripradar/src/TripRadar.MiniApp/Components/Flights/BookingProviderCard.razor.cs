using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TripRadar.MiniApp.Components.Shared;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Components.Flights;

public partial class BookingProviderCard
{
    [Parameter, EditorRequired] public FlightBookingOption Option { get; set; } = null!;
    [Parameter] public string Currency { get; set; } = "USD";

    private string? ProviderLogo => ProviderLogoResolver.Resolve(Option.AirlineLogo);
    private string ProviderInitial => ProviderLogoResolver.Initial(Option.BookWith);

    private bool HasBaggageOrSeparateTickets => Option.BaggagePrices is { Count: > 0 } || Option.SeparateTickets;

    private bool _showSeparateInfo;

    private void ShowSeparateInfo() => _showSeparateInfo = true;
    private void CloseSeparateInfo() => _showSeparateInfo = false;

    private static bool IsFree(string baggage) => baggage.Contains("free", StringComparison.OrdinalIgnoreCase);

    private string BaggageLabel(string baggage)
    {
        var label = TranslateBaggageLabel(baggage);
        return IsFree(baggage) ? $"✓ {label}" : label;
    }

    private string TranslateBaggageLabel(string baggage)
    {
        if (string.IsNullOrWhiteSpace(baggage)) return string.Empty;

        var separatorIndex = baggage.IndexOf(':');
        var name = separatorIndex >= 0 ? baggage[..separatorIndex].Trim() : baggage.Trim();
        var suffix = separatorIndex >= 0 ? baggage[separatorIndex..] : string.Empty;
        var normalized = name.ToLowerInvariant();

        var carryOnMatch = Regex.Match(normalized, @"^(?<count>\d+)\s+carry-on bag$");
        if (carryOnMatch.Success)
            return $"{string.Format(L["BookingCarryOnBagWithCount"], carryOnMatch.Groups["count"].Value)}{suffix}";

        if (normalized == "carry-on bag")
            return $"{L["BookingCarryOnBag"]}{suffix}";

        var checkedBagMatch = Regex.Match(normalized, @"^(?<ordinal>\d+(?:st|nd|rd|th))\s+checked bag$");
        if (checkedBagMatch.Success)
        {
            var ordinal = LocalizedOrdinal(checkedBagMatch.Groups["ordinal"].Value);
            return $"{string.Format(L["BookingCheckedBagWithOrdinal"], ordinal)}{suffix}";
        }

        return normalized == "checked bag" ? $"{L["BookingCheckedBag"]}{suffix}" : baggage;
    }

    private string LocalizedOrdinal(string ordinal) => ordinal switch
    {
        "1st" => L["BookingFirstOrdinal"],
        "2nd" => L["BookingSecondOrdinal"],
        "3rd" => L["BookingThirdOrdinal"],
        _ => ordinal
    };

    private static string BaggageTagCss(string baggage) =>
        IsFree(baggage)
            ? "text-[11px] bg-green-50 dark:bg-green-950 text-green-700 dark:text-green-300 border border-green-200 dark:border-green-800 px-2 py-0.5 rounded-md inline-flex items-center gap-1"
            : "text-[11px] bg-amber-50 dark:bg-amber-950 text-amber-700 dark:text-amber-300 border border-amber-200 dark:border-amber-800 px-2 py-0.5 rounded-md inline-flex items-center gap-1";

    private async Task Open()
    {
        if (string.IsNullOrEmpty(Option.Url)) return;

        if (!string.IsNullOrEmpty(Option.PostData))
            await JS.InvokeVoidAsync("tg.postOpen", Option.Url, Option.PostData);
        else
            await JS.InvokeVoidAsync("tg.open", Option.Url);
    }
}
