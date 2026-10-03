using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain;
using DigitalBrain.AI.Agents;
using DigitalBrain.Contracts;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Apps;

internal sealed class AppAgentTools(IDigitalBrain brain) : IAgentToolSource
{
    public string SourceId => "apps";

    public async Task<IAgentToolSession> OpenBoundAsync(string resource, IReadOnlyList<string> selectedToolNames, Func<AgentToolContext> context, CancellationToken ct)
    {
        var separator = resource.LastIndexOf('@');
        if (separator < 1) { throw new ArgumentException("Select an installed app revision before opening its tools.", nameof(resource)); }
        var package = PackageId.Parse(resource[..separator]);
        var selectedRevision = resource[(separator + 1)..];
        var scope = context().ScopeId;
        if (scope != DigitalBrain.Kernel.Enforcement.BrainScope.CurrentId())
        { throw new UnauthorizedAccessException("An app tool must use the caller's current brain."); }
        var app = await brain.Get<IApp>(scope + "/packages/" + package).Read().WaitAsync(ct);
        if (app.Status != AppStatus.Installed || app.UninstallPending || app.Revision is not { } revision)
        { throw new UnauthorizedAccessException("The selected app is no longer installed."); }
        if (revision.Revision != selectedRevision)
        { throw new UnauthorizedAccessException("The selected app revision changed. Discover and select it again."); }
        var manifest = (await brain.Get<IPackage>(package.ToString()).ReadRevision(revision.Revision).WaitAsync(ct)).Content.Manifest;
        return Create(selectedToolNames, context, scope, [new(package.ToString(), package, manifest.Title, manifest.Description, app)]);
    }

    public async Task<IAgentToolSession> OpenAsync(IReadOnlyList<string> selectedToolNames, Func<AgentToolContext> context, CancellationToken ct)
    {
        var selected = selectedToolNames.Where(name => name.StartsWith("app_", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0) { return new Session([]); }
        var scope = context().ScopeId;
        var installed = await InstalledApps.List(brain, scope, ct);
        return Create(selectedToolNames, context, scope, installed);
    }

    private IAgentToolSession Create(IReadOnlyList<string> selectedToolNames, Func<AgentToolContext> context, string scope, IReadOnlyList<InstalledAppSummary> installed)
    {
        var selected = selectedToolNames.ToHashSet(StringComparer.Ordinal);
        var tools = new List<AIFunction>();
        foreach (var app in installed)
        {
            foreach (var operation in app.App.Operations)
            {
                var name = AppToolName.For(app.Package, operation.Name);
                if (!selected.Contains(name)) { continue; }
                async Task<object> Invoke([Description("Input for this app operation, as text or JSON.")] string input, CancellationToken cancellationToken)
                {
                    var caller = context();
                    if (caller.ScopeId != scope || string.IsNullOrEmpty(caller.CallId))
                    { throw new InvalidOperationException("An app tool requires an active call in its brain."); }
                    var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}/{caller.RunId}/{caller.CallId}/{name}"));
                    var invocationId = new Guid(hash.AsSpan(0, 16));
                    var neuron = brain.Get<IApp>(scope + "/packages/" + app.Package);
                    var current = await neuron.Read().WaitAsync(cancellationToken);
                    if (current.Status != AppStatus.Installed || current.UninstallPending || current.Revision != app.App.Revision)
                    { throw new UnauthorizedAccessException("The selected app changed. Discover and select it again."); }
                    var invocation = await neuron.Invoke(new(invocationId, operation.Name, input, app.App.Revision)).WaitAsync(cancellationToken);
                    var started = Stopwatch.GetTimestamp();
                    while (invocation.Status == InvocationStatus.Pending)
                    {
                        if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(30))
                        { return new { isError = true, code = "app_timeout", message = "App invocation timed out.", error = "App invocation timed out.", id = invocation.Id }; }
                        await Task.Delay(100, cancellationToken);
                        invocation = await neuron.ReadInvocation(invocation.Id).WaitAsync(cancellationToken);
                    }
                    return invocation.Status == InvocationStatus.Failed
                        ? new { isError = true, code = "app_failed", message = invocation.Error ?? "The app invocation failed.", error = invocation.Error, id = invocation.Id }
                        : invocation;
                }
                tools.Add(AIFunctionFactory.Create(Invoke, name, $"{app.Title} ({app.Id}): {operation.Description}"));
            }
        }
        return new Session(tools);
    }

    private sealed class Session(IReadOnlyList<AIFunction> tools) : IAgentToolSession
    {
        public IReadOnlyList<AIFunction> Tools { get; } = tools;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
