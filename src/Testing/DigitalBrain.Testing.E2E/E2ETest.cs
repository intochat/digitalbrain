using System.Diagnostics;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Kernel;

namespace DigitalBrain.Testing.E2E;

public static class E2ETest
{
    public static E2ETestBuilder Create() => new();
    public static E2ETestBuilder<TAppHost> For<TAppHost>() where TAppHost : class => new();

    internal static async Task<E2EBrain> StartAsync<TAppHost>(IReadOnlyDictionary<string, string?> optionOverrides,
        TestExecutionOptions options, BrowserOptions browserOptions, CancellationToken cancellationToken) where TAppHost : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        options.Validate();
        var browser = Resolve(browserOptions);
        var identity = NewIdentity();
        var args = ComposeHostArguments(optionOverrides, identity);
        var session = await AspireTestSession.StartAsync<TAppHost>(args, identity, WithArtifacts(options, identity), cancellationToken).ConfigureAwait(false);
        return await ReadyAsync(new E2EBrain(session, browser), brain => brain.StartBrowserAsync(cancellationToken)).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> ComposeHostArguments(IReadOnlyDictionary<string, string?> optionOverrides, string identity)
        =>
        [
            $"{DigitalBrainHostingNames.PersistentStorageKey}=false",
            $"Orleans:ClusterId={identity}",
            .. optionOverrides.Select(pair => $"{pair.Key}={pair.Value}"),
        ];

    internal static async Task<E2EBrain> StartModulesAsync(IReadOnlyList<ModuleDefinition> modules,
        TestExecutionOptions options, BrowserOptions browserOptions, string? durableStorageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options.Validate();
        var browser = Resolve(browserOptions);
        var identity = NewIdentity();
        var session = await ModuleTestHost.StartAsync(modules, WithArtifacts(options, identity), identity, durableStorageKey, cancellationToken).ConfigureAwait(false);
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
