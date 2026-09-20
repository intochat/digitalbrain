using DigitalBrain.Core;
using DigitalBrain.Testing.Hosting;

namespace DigitalBrain.Testing.Integration;

public static class IntegrationTest
{
    public static async Task<IntegrationBrain> StartAsync(IntegrationOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        options.Execution.Validate();
        var modules = ModuleComposition.Resolve(options.Modules);
        if (modules.Count == 0) { throw new ArgumentException("Select at least one integration module.", nameof(options)); }
        var bundle = ModuleBundle.Validate(AppContext.BaseDirectory, modules);
        var identity = "test-" + Guid.NewGuid().ToString("N");
        List<string> args = [$"Runner:Directory={bundle.Directory}", $"Runner:Entry={bundle.Entry}", $"Runner:Identity={identity}"];
        for (var index = 0; index < modules.Count; index++)
        {
            args.Add($"Runner:Modules:{index}={modules[index].ModuleType.AssemblyQualifiedName}");
            foreach (var (key, value) in modules[index].Configuration) { args.Add($"Runner:Settings:{key}={value}"); }
        }
        var session = await AspireTestSession.StartAsync<Projects.DigitalBrain_Testing_ModuleAppHost>(args, identity, options.Execution, cancellationToken).ConfigureAwait(false);
        return new(session);
    }
}

public sealed class IntegrationBrain : HostedBrain
{
    internal IntegrationBrain(AspireTestSession session) : base(session) { }
    public Task RestartRuntimeAsync(CancellationToken cancellationToken = default) => Session.RestartRuntimeAsync(cancellationToken);
}
