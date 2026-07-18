using DigitalBrain.Protocol;

namespace DigitalBrain.Protocol.Microsoft.Aspire;

public interface IAspire : INeuron,
    IHandle<StartDistributedApp>,
    IEmit<DistributedAppStarted>,
    IHandle<RestartResource>,
    IEmit<ResourceRestarted>
{
    Task RestartResourceAsync(string resourceName, CancellationToken cancellationToken = default);

    Task StartNewAsync(string appHostProject = "src/DigitalBrain.AppHost", CancellationToken cancellationToken = default);

    // Domain-aware commands (Phase 2): restart the per-domain kernel resource wired via DigitalBrainDomain + AddProject + WithKernel.
    // Uses the kernel-{domain} naming from AppHost example wiring; works with IAspire grain + ResourceCommandService for dashboard/REPL control.
    Task RestartDomainKernelAsync(string domainId, CancellationToken cancellationToken = default);

    // Returns the effective Aspire dashboard URL for the current context (prefers live DistributedApplication model/allocated frontend when the IAspire grain has the real DA injected by Aspire hosting; otherwise the DIGITALBRAIN_DASHBOARD_URL or ASPIRE_DASHBOARD_URL env).
    // The value now includes the real ?t=TOKEN (set early in AppHost.cs via ASPIRE_DASHBOARD_FRONTEND_BROWSERTOKEN or captured from printed login line) so callers can copy the whole URL.
    Task<string?> GetDashboardUrlAsync(CancellationToken cancellationToken = default);

    // Orleans cluster dashboard URL (the standalone viz for grains/activations/silos that now ships with Orleans post-merge). Injected by our Aspire hosting topology (DIGITALBRAIN_ORLEANS_DASHBOARD_URL) so it starts with the kernels and auto-connects to the current digitalbrain cluster (same process).
    // Exposed via the Microsoft.Aspire neuron surface for LLM tools, MCP, clients, and in-brain experiences.
    Task<string?> GetOrleansDashboardUrlAsync(CancellationToken cancellationToken = default);
}