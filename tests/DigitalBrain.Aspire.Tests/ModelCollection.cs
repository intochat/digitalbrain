using DigitalBrain.Testing.E2E;
using Xunit;

namespace DigitalBrain.Aspire.Tests;

// One shared BrainModel per assembly. Building the AppHost model takes seconds
// (project discovery, parameter stubbing); every test against it then runs in milliseconds
// because nothing is started.
public sealed class ModelFixture : IAsyncLifetime
{
    public BrainModel Model { get; private set; } = null!;

    public async ValueTask InitializeAsync()
        => Model = await BrainModel.BuildAsync<Projects.DigitalBrain_AppHost>();

    public async ValueTask DisposeAsync() => await Model.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class ModelCollection : ICollectionFixture<ModelFixture>
{
    public const string Name = "model";
}
