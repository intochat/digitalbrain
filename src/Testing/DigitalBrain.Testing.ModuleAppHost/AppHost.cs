using Aspire.Hosting;
using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);
var directory = builder.Configuration["Runner:Directory"] ?? throw new InvalidOperationException("Runner directory is required.");
var entry = builder.Configuration["Runner:Entry"] ?? throw new InvalidOperationException("Runner dependency entry is required.");
var brain = builder.AddDigitalBrain("modules", persistentStorage: false);
builder.Configuration.AddInMemoryCollection(builder.Configuration.GetSection("Runner:Settings").AsEnumerable(true).Where(p => p.Value is not null));
foreach (var module in builder.Configuration.GetSection("Runner:Modules").GetChildren())
{
    brain.AddModuleType(Type.GetType(module.Value!, throwOnError: true)!);
}
var runtime = builder.AddExecutable("runtime", "dotnet", directory,
        "exec", "--runtimeconfig", Path.Combine(directory, entry + ".runtimeconfig.json"),
        "--depsfile", Path.Combine(directory, entry + ".deps.json"),
        Path.Combine(directory, "DigitalBrain.Testing.ModuleRunner.dll"))
    .WithReference(brain)
    .WithHttpEndpoint(name: "http", env: "ASPNETCORE_HTTP_PORTS")
    .WithHttpHealthCheck("/health")
    .AsPrimaryBrain()
    .WithEnvironment("Orleans__ClusterId", builder.Configuration["Runner:Identity"]!)
    .WithEnvironment("Orleans__ServiceId", builder.Configuration["Runner:Identity"]!);
foreach (var module in builder.Configuration.GetSection("Runner:Modules").GetChildren())
{
    runtime.WithEnvironment($"DigitalBrain__Modules__{module.Key}", module.Value!);
}
foreach (var setting in builder.Configuration.GetSection("Runner:Settings").AsEnumerable(true))
{
    if (setting.Value is not null) { runtime.WithEnvironment(setting.Key.Replace(":", "__", StringComparison.Ordinal), setting.Value); }
}
await builder.Build().RunAsync();
