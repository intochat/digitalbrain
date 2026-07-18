namespace TripRadar.MiniApp.Components.Shared;

public static class ProviderLogoResolver
{
    public static string? Resolve(params string?[] candidates) => candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

    public static string Initial(string? name)
    {
        var normalized = name?.Trim();
        return string.IsNullOrEmpty(normalized) ? "?" : normalized[..1].ToUpperInvariant();
    }
}
