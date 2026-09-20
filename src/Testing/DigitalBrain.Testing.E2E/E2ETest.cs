using System.Diagnostics;
using DigitalBrain.Core;
using DigitalBrain.Testing.Hosting;

namespace DigitalBrain.Testing.E2E;

public static class E2ETest
{
    public static E2ETestBuilder<TAppHost> For<TAppHost>() where TAppHost : class => new();

    internal static async Task<E2EBrain> StartAsync<TAppHost>(string overrides, TestExecutionOptions options,
        BrowserOptions browserOptions, CancellationToken cancellationToken) where TAppHost : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        options.Validate();
        var browser = BrowserOptionsResolver.Resolve(browserOptions,
            Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_HEADED") == "1", Debugger.IsAttached);
        var identity = "test-" + Guid.NewGuid().ToString("N");
        List<string> args = ["DigitalBrain:Testing:Enabled=true", $"Orleans:ClusterId={identity}",
            $"{CompositionOverrideTransport.ConfigurationKey}={overrides}"];
        var execution = options with
        {
            ArtifactDirectory = options.ArtifactDirectory ?? Path.Combine(AppContext.BaseDirectory, "e2e-artifacts", identity),
        };
        var session = await AspireTestSession.StartAsync<TAppHost>(args, identity, execution, cancellationToken).ConfigureAwait(false);
        return new(session, browser);
    }

}
