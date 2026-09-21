using DigitalBrain.Core;
using DigitalBrain.Testing.Hosting;

namespace DigitalBrain.Testing.Integration;

/// <summary>Shared module runner and bundle startup for integration and end-to-end tests.</summary>
public static class ModuleTestHost
{
    public static async Task<AspireTestSession> StartAsync(IReadOnlyList<ModuleDefinition> selectedModules, TestExecutionOptions execution, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedModules);
        ArgumentNullException.ThrowIfNull(execution);
        cancellationToken.ThrowIfCancellationRequested();
        execution.Validate();
        var modules = ModuleComposition.Resolve(selectedModules);
        ModuleSettingsValidation.ValidatePublicSettings(modules);
        if (modules.Count == 0) { throw new ArgumentException("Select at least one module.", nameof(selectedModules)); }
        var bundle = ModuleBundle.Validate(AppContext.BaseDirectory, modules);
        var identity = "test-" + Guid.NewGuid().ToString("N");
        List<string> args = [$"Runner:Directory={bundle.Directory}", $"Runner:Entry={bundle.Entry}", $"Runner:Identity={identity}"];
        for (var index = 0; index < modules.Count; index++)
        {
            args.Add($"Runner:Modules:{index}={modules[index].ModuleType.AssemblyQualifiedName}");
            foreach (var (key, value) in modules[index].Configuration) { args.Add($"Runner:Settings:{key}={value}"); }
        }
        var session = await AspireTestSession.StartAsync<Projects.DigitalBrain_Testing_ModuleAppHost>(args, identity, execution, cancellationToken).ConfigureAwait(false);
        return session;
    }
}
