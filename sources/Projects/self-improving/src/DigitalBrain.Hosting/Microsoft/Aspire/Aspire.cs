using DigitalBrain.Protocol.Microsoft.Aspire;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Os.Infrastructure.Orleans;

namespace DigitalBrain.Hosting.Microsoft.Aspire;

[GrainType(nameof(Aspire))]
public sealed class Aspire : Neuron, IAspire
{
    private DistributedApplication? _distributedApp;

    private DistributedApplication? DistributedApp =>
        _distributedApp ??= ServiceProvider.GetService(typeof(DistributedApplication)) as DistributedApplication;

    public async Task HandleAsync(StartDistributedApp synapse, CancellationToken cancellationToken)
    {
        var da = DistributedApp;
        if (da is null)
        {
            await Emit(new DistributedAppStarted(synapse.AppHostProjectPath, false));
            return;
        }
        await da.StartAsync(cancellationToken);
        await Emit(new DistributedAppStarted(synapse.AppHostProjectPath, true));
    }

    public async Task HandleAsync(RestartResource synapse, CancellationToken cancellationToken)
    {
        var da = DistributedApp;
        if (da is null)
        {
            await Emit(new ResourceRestarted(synapse.ResourceName, false, "No DistributedApplication in this activation context (simulation or non-Aspire host)."));
            return;
        }

        var model = da.Services.GetService(typeof(DistributedApplicationModel)) as DistributedApplicationModel;
        var resource = model?.Resources.FirstOrDefault(r => string.Equals(r.Name, synapse.ResourceName, StringComparison.OrdinalIgnoreCase));

        if (resource is null)
        {
            await Emit(new ResourceRestarted(synapse.ResourceName, false, "Resource not found."));
        }
        else
        {
            // Use non-generic + cast to avoid extra DI extension package dependency in this boundary for now.
            var commands = (ResourceCommandService)da.Services.GetService(typeof(ResourceCommandService))!;
            var outcome = await commands.ExecuteCommandAsync(resource, KnownResourceCommands.RestartCommand, cancellationToken);
            await Emit(new ResourceRestarted(synapse.ResourceName, outcome.Success, outcome.Message ?? string.Empty));
        }
    }

    // Direct methods from IAspire (REPL/script + proxy). Emit commands (timeline + will be handled by the methods above using the injected real DistributedApplication when present).
    public Task RestartResourceAsync(string resourceName, CancellationToken cancellationToken = default) =>
        Emit(new RestartResource(resourceName));

    public Task StartNewAsync(string appHostProject = "DigitalBrain.AppHost", CancellationToken cancellationToken = default) =>
        Emit(new StartDistributedApp(appHostProject));

    public Task RestartDomainKernelAsync(string domainId, CancellationToken cancellationToken = default)
    {
        // Leverage AssociatedKernelResourceName from DigitalBrainDomainResource (set via WithKernel in AppHost wiring)
        // so that restart targets the exact child kernel resource declared for the domain (e.g. "kernel-example-world")
        // instead of convention only. This makes IAspire/domain commands use the real Aspire resource graph for meta management.
        if (DistributedApp is not null)
        {
            var model = DistributedApp.Services.GetService(typeof(DistributedApplicationModel)) as DistributedApplicationModel;
            var domainRes = model?.Resources.OfType<DigitalBrainDomainResource>().FirstOrDefault(r =>
                string.Equals(r.Name, domainId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.WorldId, domainId, StringComparison.OrdinalIgnoreCase));
            if (domainRes?.AssociatedKernelResourceName is { } assoc && !string.IsNullOrWhiteSpace(assoc))
            {
                return RestartResourceAsync(assoc, cancellationToken);
            }
        }
        var fb = string.Equals(domainId, "root", StringComparison.OrdinalIgnoreCase) ? "kernel" : $"kernel-{SanitizeDomain(domainId)}";
        return RestartResourceAsync(fb, cancellationToken);
    }

    public async Task HandleAsync(UpgradeBundle synapse, CancellationToken cancellationToken)
    {
        // U5 real: for L3 promoted bundle (google-auth .AsSilo()), restart only the dedicated bundle silo resource (google-auth-silo).
        // Main brain, kernel, UI remain up (amended Core Law 3). Orleans cluster handles the handoff/retry for in-flight.
        var resourceName = synapse.BundleId == "google-auth" ? "google-auth-silo" : $"kernel-{synapse.BundleId}";
        if (DistributedApp is not null)
        {
            var model = DistributedApp.Services.GetService(typeof(DistributedApplicationModel)) as DistributedApplicationModel;
            var res = model?.Resources.FirstOrDefault(r => r.Name.Contains(synapse.BundleId, StringComparison.OrdinalIgnoreCase) || r.Name == "google-auth-silo");
            if (res is not null) resourceName = res.Name;
        }
        await RestartResourceAsync(resourceName, cancellationToken);
        await Emit(new BundleUpgraded(synapse.BundleId, synapse.Version, true));
    }

    private static string SanitizeDomain(string s) => new string(s.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());

    public Task<string?> GetDashboardUrlAsync(CancellationToken cancellationToken = default)
    {
        var da = DistributedApp;
        if (da is not null)
        {
            // IAspire neuron using the live DistributedApplication (injected by Aspire hosting into the grain's ServiceProvider for real aspire resource contexts, e.g. testing builder or when DA registers cross-process refs).
            // We can inspect the model here (DistributedApplicationModel + resources) for the aspire dashboard resource, its allocated endpoints, or to drive dashboard-specific commands in future.
            // The actual browser token for the top-level dashboard is generated + printed by the aspire CLI / AppHost layer (ASPIRE_DASHBOARD_FRONTEND_BROWSERTOKEN) and passed via ?t= on /login; it is not a first-class allocated endpoint on the model.
            // Therefore we return the env-wired value (set by AppHost or DIGITALBRAIN_DASHBOARD_URL) which callers treat as the dashboard url for this cluster.
            // (See GetDashboardUrl in clients and WorldConnectionInfo for how it flows to TUI copy links.)
            _ = da.Services.GetService(typeof(DistributedApplicationModel)) as DistributedApplicationModel;
        }

        // Prefer the per-cluster one passed down (supports per-world dashboards for launched children + root under aspire run).
        return Task.FromResult<string?>(
            Environment.GetEnvironmentVariable("DIGITALBRAIN_DASHBOARD_URL")
            ?? Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_URL")
            ?? "http://localhost:18888/login"
        );
    }

    public Task<string?> GetOrleansDashboardUrlAsync(CancellationToken cancellationToken = default)
    {
        // Prefer the one wired by domain resource / AddKernel (deferred from the kernel's http endpoint + /orleans-dashboard). Falls back for non-aspire or direct runs.
        return Task.FromResult<string?>(
            Environment.GetEnvironmentVariable("DIGITALBRAIN_ORLEANS_DASHBOARD_URL")
            ?? Environment.GetEnvironmentVariable("ASPIRE_ORLEANS_DASHBOARD_URL")
            ?? "http://localhost:8080/orleans-dashboard"
        );
    }
}
