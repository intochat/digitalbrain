using System.Diagnostics;
using DigitalBrain.Core;
using DigitalBrain.Testing.Hosting;

namespace DigitalBrain.Testing.E2E;

public static class E2ETest
{
    public static async Task<E2EBrain> StartAsync<TAppHost>(E2EOptions options,
        CancellationToken cancellationToken = default) where TAppHost : class
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Application);
        cancellationToken.ThrowIfCancellationRequested();
        options.Execution.Validate();
        var browser = BrowserOptionsResolver.Resolve(options.Browser,
            Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_HEADED") == "1", Debugger.IsAttached);
        var identity = "test-" + Guid.NewGuid().ToString("N");
        List<string> args = ["DigitalBrain:Testing:Enabled=true", $"Orleans:ClusterId={identity}"];
        args.Add($"{ApplicationConfigurationTransport.ConfigurationKey}={ApplicationConfigurationTransport.Write(options.Application)}");
        var execution = options.Execution with
        {
            ArtifactDirectory = options.Execution.ArtifactDirectory
                ?? Path.Combine(AppContext.BaseDirectory, "e2e-artifacts", identity),
        };
        var session = await AspireTestSession.StartAsync<TAppHost>(args, identity, execution, cancellationToken).ConfigureAwait(false);
        return new(session, browser);
    }
}
