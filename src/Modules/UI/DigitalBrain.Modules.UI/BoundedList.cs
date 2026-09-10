namespace DigitalBrain.UI;

internal static class BoundedList
{
    internal static List<T> Append<T>(IEnumerable<T> items, T item, int maximum)
        => [.. items.TakeLast(maximum - 1), item];
}
