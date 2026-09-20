using DigitalBrain.Core;
using DigitalBrain.Testing.Hosting;

namespace DigitalBrain.Testing.E2E;

public static class E2ETest
{
    public static Task<E2EBrain> StartAsync<TAppHost>(CancellationToken cancellationToken = default)
        where TAppHost : class => StartAsync<TAppHost>(modules: null, new(), cancellationToken);

    public static Task<E2EBrain> StartAsync<TAppHost>(IReadOnlyList<ModuleDefinition> modules, CancellationToken cancellationToken = default)
        where TAppHost : class => StartAsync<TAppHost>(modules, new(), cancellationToken);

    public static async Task<E2EBrain> StartAsync<TAppHost>(IReadOnlyList<ModuleDefinition>? modules, TestExecutionOptions execution,
        CancellationToken cancellationToken = default) where TAppHost : class
    {
        var identity = "test-" + Guid.NewGuid().ToString("N");
        List<string> args = ["DigitalBrain:Testing:Enabled=true", $"Orleans:ClusterId={identity}"];
        string? hostingKind = null;
        if (modules is not null)
        {
            foreach (var module in ModuleComposition.Resolve(modules))
            {
                foreach (var (key, value) in module.Configuration)
                {
                    if (key.StartsWith("Orleans:", StringComparison.OrdinalIgnoreCase) || key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase) || key.StartsWith("DigitalBrain:Testing:", StringComparison.OrdinalIgnoreCase))
                    { throw new ArgumentException("Application options cannot override test-owned connections or identity.", nameof(modules)); }
                    args.Add($"{key}={value}");
                    if (key.Equals("DigitalBrain:Flutter:Hosting:Kind", StringComparison.OrdinalIgnoreCase))
                    {
                        hostingKind = value;
                    }
                }
            }
        }

        if (execution.ArtifactDirectory is null)
        {
            execution = execution with
            {
                ArtifactDirectory = Path.Combine(AppContext.BaseDirectory, "e2e-artifacts", identity),
            };
        }

        var web = hostingKind is null || hostingKind.Equals("Web", StringComparison.OrdinalIgnoreCase);
        var browser = BrowserOptions.Resolve(headless: !web);
        if (execution.Browser.SlowMoMilliseconds > 0)
        {
            browser = browser with { SlowMoMilliseconds = execution.Browser.SlowMoMilliseconds };
        }

        execution = execution with { Browser = browser };
        var session = await AspireTestSession.StartAsync<TAppHost>(args, identity, execution, cancellationToken).ConfigureAwait(false);
        return new(session);
    }
}
