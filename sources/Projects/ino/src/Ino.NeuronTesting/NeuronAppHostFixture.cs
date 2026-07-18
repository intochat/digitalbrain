using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace Ino.NeuronTesting;

// Boots the AppHost via DistributedApplicationTestingBuilder. Per-test-class
// scope (xUnit IClassFixture). Discovers ProjectResources from the AppHost
// graph — adding a project to TAppHost requires zero changes here.
//
// Test mode is encoded structurally via TAppHost: the consumer types over
// Projects.Ino_AppHost_Testing (which stamps Ino:Mode = Testing on every
// silo through Aspire's WithEnvironment chain) rather than over the
// production AppHost. No process-environment mutation, no fixture-id keying
// — both were artifacts of the old "fixture flips a global flag" model and
// disappear once the AppHost itself owns the test-mode posture.
public sealed class NeuronAppHostFixture<TAppHost> : IAsyncLifetime
    where TAppHost : class
{
    public DistributedApplication App { get; private set; } = null!;
    public string KernelGrpcUrl { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<TAppHost>();

        foreach (var p in builder.Resources.OfType<ParameterResource>())
            builder.Configuration[$"Parameters:{p.Name}"] = "test";

        App = await builder.BuildAsync();
        await App.StartAsync();

        var siloNames = builder.Resources
            .OfType<ProjectResource>()
            .Where(r => !r.Name.StartsWith("telegram", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Name)
            .ToArray();

        await Task.WhenAll(siloNames.Select(name =>
            App.ResourceNotifications.WaitForResourceHealthyAsync(name)));

        KernelGrpcUrl = App.GetEndpoint("kernel", "https").ToString();
    }

    public async ValueTask DisposeAsync()
    {
        if (App is not null)
            await App.DisposeAsync();
    }
}
