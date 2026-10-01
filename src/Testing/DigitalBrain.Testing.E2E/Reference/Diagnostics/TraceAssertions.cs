namespace DigitalBrain.Testing.E2E.Diagnostics;

public static class TraceAssertions
{
    public static string TextOf(CapturedSpan span)
        => string.Join("\n", span.Attributes.Select(pair => pair.Key + "=" + pair.Value));

    public static string TextOf(CapturedLog log)
        => log.Body + "\n" + string.Join("\n", log.Attributes.Select(pair => pair.Key + "=" + pair.Value));

    public static bool IsGenAiSpan(CapturedSpan span)
        => span.Scope.StartsWith("DigitalBrain.AI", StringComparison.Ordinal)
            || span.Scope.StartsWith("Microsoft.Extensions.AI", StringComparison.Ordinal)
            || span.Scope.StartsWith("Experimental.Microsoft.Extensions.AI", StringComparison.Ordinal);

    public static Task WaitForAsync(Func<bool> condition, CancellationToken ct)
        => WaitForAsync(condition, TimeSpan.FromSeconds(20), ct);

    public static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }
    }
}
