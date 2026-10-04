using DigitalBrain.Google.Gmail;
using DigitalBrain.Testing.Module;
using Xunit;

namespace DigitalBrain.Modules.Google.Gmail.Tests;

// One in-process brain with the Gmail module, a configured registration, a stubbed Google token
// endpoint and the product HTTP pipeline. The collection's facts share it; a fact that needs a
// different registration (for example an unconfigured one) starts its own brain — in-process,
// that costs seconds, not a container boot.
public sealed class GmailHostFixture : IAsyncLifetime
{
    private TokenEndpointStub? _stub;
    private ModuleBrain? _brain;

    public ModuleBrain Brain => _brain ?? throw new InvalidOperationException("The Gmail host has not started yet.");

    public async ValueTask InitializeAsync()
    {
        _stub = await TokenEndpointStub.StartAsync(CancellationToken.None);
        _brain = await ModuleTest.Create()
            .WithModule<GmailModule, GmailModuleOptions>(options => options.TokenEndpoint = _stub.TokenEndpoint)
            .WithExecution(new() { PrivateConfiguration = ConfiguredRegistration(_stub) })
            .WithHttpEdge()
            .StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_brain is not null) { await _brain.DisposeAsync(); }
        if (_stub is not null) { await _stub.DisposeAsync(); }
    }

    internal static Dictionary<string, string?> ConfiguredRegistration(TokenEndpointStub stub) => new()
    {
        ["DigitalBrain:Integrations:gmail:ClientId"] = "integration-client",
        ["DigitalBrain:Integrations:gmail:ClientSecret"] = "integration-secret",
        ["DigitalBrain:Integrations:gmail:PublicOrigin"] = stub.Origin.AbsoluteUri,
    };
}

[CollectionDefinition(Name)]
public sealed class GmailHostCollection : ICollectionFixture<GmailHostFixture>
{
    public const string Name = "gmail-configured-host";
}
