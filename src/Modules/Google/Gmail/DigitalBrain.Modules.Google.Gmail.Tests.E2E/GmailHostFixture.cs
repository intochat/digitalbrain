using DigitalBrain.Core;
using DigitalBrain.Google.Gmail;
using DigitalBrain.Testing.E2E;
using Xunit;

namespace DigitalBrain.Modules.Google.Gmail.Tests.E2E;

public sealed class GmailHostFixture : SharedBrainFixture
{
    private readonly Lazy<Task<TokenEndpointStub>> _tokenEndpoint = new(() => TokenEndpointStub.StartAsync(CancellationToken.None));

    protected override async Task<E2EBrain> StartHostAsync(CancellationToken cancellationToken)
    {
        var stub = await _tokenEndpoint.Value;
        return await E2ETest.Create()
            .WithModule<DigitalBrain.Platform.Secrets.SecretsModule>()
            .WithModule<DigitalBrain.Platform.Integrations.IntegrationsModule>()
            .WithModule<GmailModule, GmailModuleOptions>(options => options.TokenEndpoint = stub.TokenEndpoint)
            .WithExecution(new() { PrivateConfiguration = ConfiguredRegistration(stub) })
            .StartAsync(cancellationToken);
    }

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_tokenEndpoint.IsValueCreated) { await (await _tokenEndpoint.Value).DisposeAsync(); }
    }

    private static Dictionary<string, string?> ConfiguredRegistration(TokenEndpointStub stub) => new()
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
