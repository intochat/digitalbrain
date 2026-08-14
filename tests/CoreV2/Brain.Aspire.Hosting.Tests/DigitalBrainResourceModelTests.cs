using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using Xunit;

namespace Brain.Aspire.Hosting.Tests;

public sealed class DigitalBrainResourceModelTests
{
    [Fact]
    public void AddDigitalBrain_declares_the_complete_durable_fabric()
    {
        var builder = DistributedApplication.CreateBuilder();

        _ = builder.AddDigitalBrain("brain");

        var names = builder.Resources.Select(resource => resource.Name).ToArray();
        Assert.Contains("storage", names);
        Assert.Contains("clustering", names);
        Assert.Contains("reminders", names);
        Assert.Contains("grainstate", names);
        Assert.Contains("journal", names);
        Assert.Contains("brain", names);
    }

    [Fact]
    public void Client_reference_is_distinct_from_silo_reference()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");

        Assert.IsType<DigitalBrainClientReference>(brain.AsClient());
    }

    [Fact]
    public void Module_projection_is_applied_only_to_the_selected_resource_role()
    {
        var clientBuilder = DistributedApplication.CreateBuilder();
        var clientBrain = clientBuilder.AddDigitalBrain("client-brain");
        var clientProjection = new RecordingProjection();
        clientBrain.AddModule<Marker>(module => module.AddProjection(clientProjection));

        clientBuilder
            .AddExecutable("client", "dotnet", ".")
            .WithReference(clientBrain.AsClient());

        Assert.True(clientProjection.ClientApplied);
        Assert.False(clientProjection.RuntimeApplied);

        var runtimeBuilder = DistributedApplication.CreateBuilder();
        var runtimeBrain = runtimeBuilder.AddDigitalBrain("runtime-brain");
        var runtimeProjection = new RecordingProjection();
        runtimeBrain.AddModule<Marker>(module => module.AddProjection(runtimeProjection));

        runtimeBuilder
            .AddExecutable("runtime", "dotnet", ".")
            .WithReference(runtimeBrain);

        Assert.True(runtimeProjection.RuntimeApplied);
        Assert.False(runtimeProjection.ClientApplied);
    }

    private sealed class Marker;

    private sealed class RecordingProjection : DigitalBrainModuleProjection
    {
        public bool RuntimeApplied { get; private set; }

        public bool ClientApplied { get; private set; }

        public override void ApplyToRuntime<TResource>(IResourceBuilder<TResource> builder)
            => RuntimeApplied = true;

        public override void ApplyToClient<TResource>(IResourceBuilder<TResource> builder)
            => ClientApplied = true;
    }
}
