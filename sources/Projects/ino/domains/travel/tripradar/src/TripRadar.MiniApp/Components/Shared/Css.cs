namespace TripRadar.MiniApp.Components.Shared;

public static class Css
{
    public static string Join(params string?[] parts)
    {
        return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
    }
}
