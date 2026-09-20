using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DigitalBrain.Testing.Framework.Tests")]

namespace DigitalBrain.Testing.E2E;

internal sealed record ResolvedBrowserOptions(bool Headless, float SlowMoMilliseconds,
    TimeSpan StartupTimeout, TimeSpan AssertionTimeout);

internal static class BrowserOptionsResolver
{
    internal static ResolvedBrowserOptions Resolve(BrowserOptions options, bool headedRequested, bool debuggerAttached)
    {
        TestExecutionOptions.ValidateTimeout(options.StartupTimeout);
        TestExecutionOptions.ValidateTimeout(options.AssertionTimeout);
        var headless = options.Headless ?? !(headedRequested || debuggerAttached);
        var delay = options.SlowMoMilliseconds ?? (headless ? 0 : 250);
        if (!float.IsFinite(delay) || delay < 0) { throw new ArgumentOutOfRangeException(nameof(options)); }
        return new(headless, delay, options.StartupTimeout, options.AssertionTimeout);
    }
}
