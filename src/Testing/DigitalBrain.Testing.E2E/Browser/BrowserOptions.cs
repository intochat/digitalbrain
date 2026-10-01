using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DigitalBrain.Core.Tests.E2E")]

namespace DigitalBrain.Testing.E2E;

public sealed record BrowserOptions
{
    public bool? Headless { get; init; }
    public float? SlowMoMilliseconds { get; init; }
    // A shared host serves many facts, each opening its own context; an idle primary session
    // would keep syncing shell state against theirs. Turn it off to lease browsers per fact.
    public bool PrimarySession { get; init; } = true;
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan AssertionTimeout { get; init; } = TimeSpan.FromSeconds(60);
}

internal sealed record ResolvedBrowserOptions(bool Headless, float SlowMoMilliseconds,
    TimeSpan StartupTimeout, TimeSpan AssertionTimeout, bool PrimarySession);

internal static class BrowserOptionsResolver
{
    internal static ResolvedBrowserOptions Resolve(BrowserOptions options, bool headedRequested, bool debuggerAttached)
    {
        TestExecutionOptions.ValidateTimeout(options.StartupTimeout);
        TestExecutionOptions.ValidateTimeout(options.AssertionTimeout);
        var headless = options.Headless ?? !(headedRequested || debuggerAttached);
        var delay = options.SlowMoMilliseconds ?? (headless ? 0 : 250);
        if (!float.IsFinite(delay) || delay < 0) { throw new ArgumentOutOfRangeException(nameof(options)); }
        return new(headless, delay, options.StartupTimeout, options.AssertionTimeout, options.PrimarySession);
    }
}
