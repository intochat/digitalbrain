using DigitalBrain.Core;
using DigitalBrain.Testing.Hosting;

namespace DigitalBrain.Testing.E2E;

public static class E2ETest
{
    public static Task<E2EBrain> StartAsync<TAppHost>(IApplicationConfiguration options, CancellationToken cancellationToken = default)
        where TAppHost : class => StartAsync<TAppHost>(options, new(), cancellationToken);

    public static async Task<E2EBrain> StartAsync<TAppHost>(IApplicationConfiguration options, TestExecutionOptions execution,
        CancellationToken cancellationToken = default) where TAppHost : class
    {
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = options.CreateSnapshot();
        var identity = "test-" + Guid.NewGuid().ToString("N");
        List<string> args = [];
        foreach (var (key, value) in snapshot.Configuration)
        {
            if (key.StartsWith("Orleans:", StringComparison.OrdinalIgnoreCase) || key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase) || key.StartsWith("DigitalBrain:Testing:", StringComparison.OrdinalIgnoreCase))
            { throw new ArgumentException("Application options cannot override test-owned connections or identity.", nameof(options)); }
            args.Add($"{key}={value}");
        }
        args.Add("DigitalBrain:Testing:Enabled=true");
        args.Add($"Orleans:ClusterId={identity}");
        execution = WithHostedBrowser(snapshot, execution, identity);
        var session = await AspireTestSession.StartAsync<TAppHost>(args, identity, execution, cancellationToken).ConfigureAwait(false);
        return new(session);
    }

    private static TestExecutionOptions WithHostedBrowser(ApplicationConfigurationSnapshot snapshot, TestExecutionOptions execution, string identity)
    {
        if (execution.ArtifactDirectory is null)
        {
            execution = execution with
            {
                ArtifactDirectory = Path.Combine(AppContext.BaseDirectory, "e2e-artifacts", identity),
            };
        }

        if (!snapshot.Configuration.TryGetValue("DigitalBrain:Flutter:Hosting:Kind", out var kind) || kind is null)
        {
            return execution;
        }

        var web = kind.Equals("Web", StringComparison.OrdinalIgnoreCase);
        return execution with { Browser = BrowserOptions.Resolve(headless: !web) };
    }
}
