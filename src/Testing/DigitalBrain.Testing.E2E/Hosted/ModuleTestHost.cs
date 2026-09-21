using System.Reflection;
using Aspire.Hosting;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Core;

namespace DigitalBrain.Testing.E2E;

internal static class ModuleTestHost
{
    public static Task<AspireTestSession> StartAsync(IReadOnlyList<ModuleDefinition> selectedModules,
        TestExecutionOptions execution, string identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedModules);
        ArgumentNullException.ThrowIfNull(execution);
        cancellationToken.ThrowIfCancellationRequested();
        execution.Validate();
        var modules = ModuleComposition.Resolve(selectedModules);
        ModuleSettingsValidation.ValidatePublicSettings(modules);
        if (modules.Count == 0) { throw new ArgumentException("Select at least one module.", nameof(selectedModules)); }

        var launch = ResolveModuleHostLaunch();
        return AspireTestSession.StartAsync(identity, execution, builder =>
        {
            var brain = builder.AddDigitalBrain("modules", persistentStorage: false);
            brain.AddModules(modules);
            builder.AddExecutable("runtime", "dotnet", launch.WorkingDirectory, launch.Arguments)
                .WithReference(brain)
                .WithHttpEndpoint(name: "http", env: "ASPNETCORE_HTTP_PORTS")
                .WithHttpHealthCheck(ModuleHostEndpoints.Health)
                .AsPrimaryBrain()
                .WithEnvironment("Orleans__ClusterId", identity)
                .WithEnvironment("Orleans__ServiceId", identity);
        }, cancellationToken);
    }

    private static (string WorkingDirectory, string[] Arguments) ResolveModuleHostLaunch()
    {
        var testAssembly = Assembly.GetEntryAssembly()?.GetName().Name
            ?? throw new InvalidOperationException("Hosted tests must run from a test assembly entry point.");
        var directory = AppContext.BaseDirectory;
        var moduleHost = typeof(ModuleTestHost).Assembly.GetName().Name + ".dll";
        foreach (var required in new[] { testAssembly + ".runtimeconfig.json", testAssembly + ".deps.json", moduleHost })
        {
            if (!File.Exists(Path.Combine(directory, required)))
            { throw new InvalidOperationException($"Missing module host asset '{required}'. Build the test project first."); }
        }
        return (directory, [
            "exec",
            "--runtimeconfig", Path.Combine(directory, testAssembly + ".runtimeconfig.json"),
            "--depsfile", Path.Combine(directory, testAssembly + ".deps.json"),
            Path.Combine(directory, moduleHost)]);
    }
}
