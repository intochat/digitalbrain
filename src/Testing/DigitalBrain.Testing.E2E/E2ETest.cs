using System.Diagnostics;
using DigitalBrain.Core;

namespace DigitalBrain.Testing.E2E;

public static class E2ETest
{
    public static E2ETestBuilder Create() => new();
    public static E2ETestBuilder<TAppHost> For<TAppHost>() where TAppHost : class => new();

    internal static async Task<E2EBrain> StartAsync<TAppHost>(string overrides, TestExecutionOptions options,
        BrowserOptions browserOptions, CancellationToken cancellationToken) where TAppHost : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        options.Validate();
        var browser = Resolve(browserOptions);
        var identity = NewIdentity();
        List<string> args = ["DigitalBrain:Testing:Enabled=true", $"Orleans:ClusterId={identity}",
            $"{CompositionOverrideTransport.ConfigurationKey}={overrides}"];
        var session = await AspireTestSession.StartAsync<TAppHost>(args, identity, WithArtifacts(options, identity), cancellationToken).ConfigureAwait(false);
        return await ReadyAsync(new E2EBrain(session, browser), brain => brain.StartBrowserAsync(cancellationToken)).ConfigureAwait(false);
    }

    internal static async Task<E2EBrain> StartModulesAsync(IReadOnlyList<ModuleDefinition> modules,
        TestExecutionOptions options, BrowserOptions browserOptions, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options.Validate();
        var browser = Resolve(browserOptions);
        var identity = NewIdentity();
        var session = await ModuleTestHost.StartAsync(modules, WithArtifacts(options, identity), identity, cancellationToken).ConfigureAwait(false);
        return await ReadyAsync(new E2EBrain(session, browser), brain => brain.StartBrowserAsync(cancellationToken)).ConfigureAwait(false);
    }

    private static string NewIdentity() => "test-" + Guid.NewGuid().ToString("N");

    private static ResolvedBrowserOptions Resolve(BrowserOptions options) => BrowserOptionsResolver.Resolve(options,
        Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_HEADED") == "1", Debugger.IsAttached);

    private static TestExecutionOptions WithArtifacts(TestExecutionOptions options, string identity) => options with
    {
        ArtifactDirectory = options.ArtifactDirectory ?? Path.Combine(AppContext.BaseDirectory, "e2e-artifacts", identity),
    };

    internal static async Task<T> ReadyAsync<T>(T owner, Func<T, Task> initialize) where T : IAsyncDisposable
    {
        try
        {
            await initialize(owner).ConfigureAwait(false);
            return owner;
        }
        catch (Exception error)
        {
            try { await owner.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { error.Data["StartupCleanupFailure"] = cleanup; }
            throw;
        }
    }
}
