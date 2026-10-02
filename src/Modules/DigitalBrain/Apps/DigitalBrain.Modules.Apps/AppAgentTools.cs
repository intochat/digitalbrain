using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.AI.Agents;
using DigitalBrain.Contracts;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Apps;

internal sealed class AppAgentTools(IDigitalBrain brain) : IAgentToolSource
{
    public async Task<IAgentToolSession> OpenAsync(IReadOnlyList<string> selectedToolNames, Func<AgentToolContext> context, CancellationToken ct)
    {
        var selected = selectedToolNames.Where(name => name.StartsWith("app_", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0) { return new Session([]); }
        var scope = context().ScopeId;
        var installed = await InstalledApps.List(brain, scope, ct);
        var tools = new List<AIFunction>();
        foreach (var app in installed)
        {
            foreach (var operation in app.App.Operations)
            {
                var name = AppToolName.For(app.Package, operation.Name);
                if (!selected.Contains(name)) { continue; }
                async Task<AppInvocation> Invoke([Description("Input for this app operation, as text or JSON.")] string input, CancellationToken cancellationToken)
                {
                    var caller = context();
                    if (caller.ScopeId != scope || string.IsNullOrEmpty(caller.CallId))
                    { throw new InvalidOperationException("An app tool requires an active call in its brain."); }
                    var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}/{caller.RunId}/{caller.CallId}/{name}"));
                    var invocationId = new Guid(hash.AsSpan(0, 16));
                    var neuron = brain.Get<IApp>(scope + "/packages/" + app.Package);
                    var invocation = await neuron.Invoke(new(invocationId, operation.Name, input)).WaitAsync(cancellationToken);
                    var started = Stopwatch.GetTimestamp();
                    while (invocation.Status == InvocationStatus.Pending)
                    {
                        if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(30))
                        { return invocation with { Status = InvocationStatus.Failed, Error = "App invocation timed out.", CompletedAt = DateTimeOffset.UtcNow }; }
                        await Task.Delay(100, cancellationToken);
                        invocation = await neuron.ReadInvocation(invocation.Id).WaitAsync(cancellationToken);
                    }
                    return invocation;
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
