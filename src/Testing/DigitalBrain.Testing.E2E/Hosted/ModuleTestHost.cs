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

        var launch = ResolveModuleHostLaunch(modules);
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

    // The module runtime is this test assembly relaunched against its own dependency closure, so
    // everything it needs must already sit in the test output. Checking here turns a silent
    // three-minute wait for a child process that cannot load its modules into an immediate error.
    private static (string WorkingDirectory, string[] Arguments) ResolveModuleHostLaunch(IReadOnlyList<ModuleDefinition> modules)
    {
        var testAssembly = Assembly.GetEntryAssembly()?.GetName().Name
            ?? throw new InvalidOperationException(
                "Hosted tests need the test assembly as the process entry point. Run them through Microsoft.Testing.Platform.");
        var directory = AppContext.BaseDirectory;
        var moduleHost = typeof(ModuleTestHost).Assembly.GetName().Name + ".dll";
        foreach (var required in new[] { testAssembly + ".runtimeconfig.json", testAssembly + ".deps.json", moduleHost })
        {
            if (!File.Exists(Path.Combine(directory, required)))
            { throw new InvalidOperationException($"Missing module host asset '{required}'. Build the test project first."); }
        }
        foreach (var module in modules)
        {
            var expected = module.ModuleType.Assembly.GetName();
            var asset = Path.Combine(directory, expected.Name + ".dll");
            if (!File.Exists(asset))
            {
                throw new InvalidOperationException(
                    $"Module assembly '{expected.Name}' is not in the test output. Reference the module project from the test project.");
            }
            if (AssemblyName.GetAssemblyName(asset).FullName != expected.FullName)
            {
                throw new InvalidOperationException(
                    $"Module assembly '{expected.Name}' in the test output does not match the one loaded here.");
            }
        }
        return (directory, [
            "exec",
            "--runtimeconfig", Path.Combine(directory, testAssembly + ".runtimeconfig.json"),
            "--depsfile", Path.Combine(directory, testAssembly + ".deps.json"),
            Path.Combine(directory, moduleHost)]);
    }
}
