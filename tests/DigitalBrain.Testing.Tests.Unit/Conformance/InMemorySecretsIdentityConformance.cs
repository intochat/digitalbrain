using DigitalBrain.Capabilities;
using DigitalBrain.Testing.Conformance;
using DigitalBrain.Testing.Conformance.Identity;
using DigitalBrain.Testing.Conformance.Secrets;

namespace DigitalBrain.Testing.Tests.Unit.Conformance;

// The reference subject: InMemorySecrets must pass every secrets@1 fact.
public sealed class InMemorySecretsConformance : SecretsConformance<InMemorySecrets>
{
    protected override Task<SecretsSubject> Connect(CancellationToken ct)
    {
        var callers = new AmbientCallerScope();
        var secrets = new InMemorySecrets(callers) { Declarations = KitDeclarations.Identity };
        secrets.Provide(KitDeclarations.NamedSecret.Name, KitDeclarations.NamedSecret.Value);
        return Task.FromResult(new SecretsSubject(secrets, callers, () => secrets.Log) { Named = KitDeclarations.NamedSecret });
    }
}

// The TestOS secrets fake passes the kit too.
public sealed class FakeSecretsConformance : SecretsConformance<FakeSecrets>
{
    protected override Task<SecretsSubject> Connect(CancellationToken ct)
    {
        var callers = new AmbientCallerScope();
        var secrets = new FakeSecrets(callers) { Declarations = KitDeclarations.Identity };
        secrets.Provide(KitDeclarations.NamedSecret.Name, KitDeclarations.NamedSecret.Value);
        return Task.FromResult(new SecretsSubject(secrets, callers, () => secrets.Log) { Named = KitDeclarations.NamedSecret });
    }
}

// The reference subject: InMemoryIdentity must pass every identity@1 fact.
public sealed class InMemoryIdentityConformance : IdentityConformance<InMemoryIdentity>
{
    protected override Task<IdentitySubject> Connect(CancellationToken ct)
    {
        var callers = new AmbientCallerScope();
        var identity = new InMemoryIdentity(callers) { Declarations = KitDeclarations.Identity };
        return Task.FromResult(new IdentitySubject(identity, identity, callers, identity.SignIn)
        {
            Sessions = identity,
            Description = identity,
            PrivilegedModule = KitDeclarations.PrivilegedModule,
        });
    }
}

// The TestOS identity fake passes the kit too.
public sealed class FakeIdentityConformance : IdentityConformance<FakeIdentity>
{
    protected override Task<IdentitySubject> Connect(CancellationToken ct)
    {
        var callers = new AmbientCallerScope();
        var identity = new FakeIdentity(callers) { Declarations = KitDeclarations.Identity };
        return Task.FromResult(new IdentitySubject(identity, identity, callers, identity.SignIn)
        {
            Sessions = identity,
            Description = identity,
            PrivilegedModule = KitDeclarations.PrivilegedModule,
        });
    }
}

// The manifest declarations the 09-03 kit facts run under: one module granted the identity@1 session members and
// declaring the kit-issuer secret.
internal static class KitDeclarations
{
    public const string PrivilegedModule = "kit-auth";

    public static KitNamedSecret NamedSecret { get; } = new(PrivilegedModule, "kit-issuer:SigningKey", $"v-{Guid.NewGuid():N}");

    public static ModuleDeclarations Identity { get; } = new([new ModuleManifest([], [], [], [], [Secret.Named("kit-issuer")])
    {
        Id = PrivilegedModule,
        Privileged = [new(Capability.Identity.V1, "Issue"), new(Capability.Identity.V1, "End"), new(Capability.Identity.V1, "Validate")],
    }]);
}
