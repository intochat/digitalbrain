using Microsoft.Extensions.Localization;
using TripRadar.MiniApp.Client.Infrastructure.Localization;

namespace TripRadar.MiniApp.Client.Infrastructure.Services.Localization;

public sealed class CountryNameLocalizer(IStringLocalizer<SharedResource> localizer)
{
    public string GetName(string? isoCode)
    {
        if (string.IsNullOrWhiteSpace(isoCode))
            return string.Empty;

        var key = $"Country_{isoCode.Trim().ToUpperInvariant()}";
        var localized = localizer[key];
        return localized.ResourceNotFound ? isoCode.Trim().ToUpperInvariant() : localized.Value;
    }
}
