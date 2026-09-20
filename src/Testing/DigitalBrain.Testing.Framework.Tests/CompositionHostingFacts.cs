using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Core;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.AI;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.Flutter;
using DigitalBrain.Supabase;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using DigitalBrain.Flutter.Aspire.Hosting;

namespace DigitalBrain.Tests;

public sealed class CompositionHostingFacts
{
    [Fact]
    public async Task WebReadinessWaitsForCurrentCompilationInsteadOfCachedAssets()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DigitalBrain.slnx"))) { root = root.Parent; }
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        var brain = builder.AddDigitalBrain("brain", persistentStorage: false)
            .WithModule<FlutterModule>(flutter => flutter.WithOptions(new() { Hosting = new() { Kind = FlutterHostKind.Web, WorkingDirectory = Path.Combine(root!.FullName, "src/Modules/Flutter/app/core") } }));
        builder.AddExecutable("runtime", "unused", ".").WithReference(brain);
        var host = Assert.Single(builder.Resources, resource => resource.Name == "FlutterShell");
        Assert.Contains(host.Annotations.OfType<HealthCheckAnnotation>(), annotation => annotation.Key == "FlutterShell-build");
        var lines = new List<string>();
        async IAsyncEnumerable<string> ReadLines([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            foreach (var line in lines) { ct.ThrowIfCancellationRequested(); yield return line; }
        }
        var check = new FlutterWebBuildHealthCheck(ReadLines);
        async Task<HealthStatus> Status() => (await check.CheckHealthAsync(new(), TestContext.Current.CancellationToken)).Status;
        Assert.Equal(HealthStatus.Unhealthy, await Status());
        lines.Add("Launching lib\\main.dart on Web Server in release mode...");
        Assert.Equal(HealthStatus.Unhealthy, await Status());
        lines.Add("lib\\main.dart is being served at http://127.0.0.1:12345");
        Assert.Equal(HealthStatus.Healthy, await Status());
        lines.Add("[sys] Starting process...: Cmd = flutter");
        Assert.Equal(HealthStatus.Unhealthy, await Status());
    }
    [Fact]
    public async Task ExplicitSupabaseParameterWinsOverAmbientConnection()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:supabase"] = "ambient" });
        var brain = builder.AddDigitalBrain("brain", persistentStorage: false).WithModule<SupabaseModule>(db => db.WithConnection("supabase"));
        builder.AddExecutable("runtime", "unused", ".").WithReference(brain);
        // Test secrets arrive after graph construction, before resource evaluation.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Parameters:supabase-connection"] = "explicit" });
        var parameter = Assert.IsType<ParameterResource>(Assert.Single(builder.Resources, r => r.Name == "supabase-connection"));
        Assert.Equal("explicit", await parameter.GetValueAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void WebOverrideCreatesOnlyOneHostWhenClientReferencesFirst()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DigitalBrain.slnx"))) { root = root.Parent; }
        Assert.NotNull(root);
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        var brain = builder.AddDigitalBrain("brain", persistentStorage: false)
            .WithModule<FlutterModule>(f => f.WithOptions(new()
            {
                Hosting = new() { Kind = FlutterHostKind.Window, WorkingDirectory = Path.Combine(root.FullName, "src/Modules/Flutter/app/core") },
            }))
            .ConfigureModule<FlutterModule>(f => f.WithWebHost());
        Assert.DoesNotContain(builder.Resources, r => r.Name == "FlutterShell");
        builder.AddExecutable("client", "unused", ".").WithReference(brain.AsClient());
        builder.AddExecutable("runtime", "unused", ".").WithReference(brain);
        var host = Assert.Single(builder.Resources, r => r.Name == "FlutterShell");
        Assert.Single(host.Annotations.OfType<BrainBrowserAnnotation>());
    }

    [Fact]
    public void ProfilesAndExplicitDefaultsSurviveTypedCompilation()
    {
        var options = new AIOptions { Default = new() { Profile = "local", MaxOutputTokens = 512 }, Telemetry = new() { EnableSensitiveData = false } };
        options.ModelProfiles.Add("local", new() { Provider = "OpenAI", Model = "fixture", Endpoint = "http://localhost:8123/v1", MaxOutputTokens = 256 });
        var module = Assert.Single(new BrainCompositionBuilder().WithModule<AIModule>(ai => ai.WithOptions(options)).Build().Modules);
        Assert.Equal("http://localhost:8123/v1", module.Configuration["DigitalBrain:AI:ModelProfiles:local:Endpoint"]);
        Assert.Equal("256", module.Configuration["DigitalBrain:AI:ModelProfiles:local:MaxOutputTokens"]);
        Assert.Equal("512", module.Configuration["DigitalBrain:AI:Default:MaxOutputTokens"]);
        Assert.Equal("False", module.Configuration["DigitalBrain:AI:Telemetry:EnableSensitiveData"]);
    }

    [Fact]
    public void TypedModuleFieldsReachCompiledRuntimeSettings()
    {
        var modules = new BrainCompositionBuilder()
            .WithModule<AIModule>(ai => ai.WithDefaultLlm<IGpt56Luna>()
                .WithModelEndpoint(AiProvider.OpenAI, new("http://localhost:8123/v1")))
            .WithModule<SupabaseModule>(db => db.WithPostgres())
            .WithModule<FlutterModule>(f => f.WithWindowHost())
            .ConfigureModule<FlutterModule>(f => f.WithWebHost()).Build().Modules;
        var settings = modules.SelectMany(m => m.Configuration).ToDictionary(p => p.Key, p => p.Value);
        Assert.Equal("Web", settings["DigitalBrain:Flutter:Hosting:Kind"]);
        Assert.Equal("http://localhost:8123/v1", settings["DigitalBrain:AI:OpenAI:Endpoint"]);
        Assert.Equal(nameof(IGpt56Luna), settings["DigitalBrain:AI:Default:Model"]);
        Assert.Equal("Postgres", settings["DigitalBrain:Supabase:Hosting:Kind"]);
    }

    [Fact]
    public void HostDefersModuleResourcesAndFreezesOnFirstReference()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Parameters:openai-api-key"] = "synthetic" });
        var brain = builder.AddDigitalBrain("brain", persistentStorage: false)
            .WithModule<AIModule>(ai => ai.WithDefaultLlm<IGpt56Luna>())
            .WithModule<SupabaseModule>(db => db.WithPostgres());
        Assert.DoesNotContain(builder.Resources, r => r.Name == "supabase-postgres");
        builder.AddExecutable("runtime", "unused", ".").WithReference(brain);
        Assert.Single(builder.Resources, r => r.Name == "supabase-postgres");
        builder.AddExecutable("client", "unused", ".").WithReference(brain.AsClient());
        Assert.Single(builder.Resources, r => r.Name == "supabase-postgres");
        Assert.Throws<InvalidOperationException>(() => brain.ConfigureModule<SupabaseModule>(db => db.WithConnection("other")));
    }
}
