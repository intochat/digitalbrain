using System.Security.Claims;
using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Core;
using DigitalBrain.Sdk.Integrations;
using DigitalBrain.Platform.Integrations;
using DigitalBrain.Sdk.Secrets;
using DigitalBrain.Platform.Secrets;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Unit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Hosting;
using Xunit;

namespace DigitalBrain.Core.Tests.Unit.Integrations;

public sealed class FakeGoogleModule : IModule
{
    public static IntegrationDefinition Integration { get; } = IntegrationDefinition.For("google", "Google")
        .RequiresSecret("ClientId")
        .RequiresSecret("ClientSecret")
        .RequiresSetting("RedirectPath");

    public void Configure(ISiloBuilder silo) { }
}

public sealed class FakeRivalGoogleModule : IModule
{
    public static IntegrationDefinition Integration { get; } = IntegrationDefinition.For("google", "Rival Google");

    public void Configure(ISiloBuilder silo) { }
}

public sealed class FakeAiModule : IModule
{
    public static IntegrationDefinition[] Integrations { get; } =
    [
        IntegrationDefinition.For("openai", "OpenAI").RequiresSecret("ApiKey"),
        IntegrationDefinition.For("anthropic", "Anthropic").RequiresSecret("ApiKey"),
    ];

    public void Configure(ISiloBuilder silo) { }
}

public sealed class RegistrationFacts
{
    private const string Canary = "canary-registration-9d41c7";
    private const string OtherCanary = "canary-operator-55be02";

    [Fact]
    public async Task ConfiguringAllFieldsMakesTheRegistrationReadyAndSignals()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var registration = Registration(brain, "google");
        await using var changes = await brain.Observe<RegistrationChanged>(registration, ct);

        var snapshot = await registration.Configure(Values(("ClientId", Canary + "-id"), ("ClientSecret", Canary), ("RedirectPath", "/callback")));

        Assert.Equal(RegistrationStatus.Ready, snapshot.Status);
        Assert.Empty(snapshot.MissingFields);
        var signal = await changes.NextAsync(ct: ct);
        Assert.Equal("google", signal.IntegrationId);
        Assert.Equal(RegistrationStatus.Ready, signal.Status);

        Assert.Equal(
            ["IntegrationId", "MissingFields", "Revision", "Settings", "Status"],
            typeof(RegistrationSnapshot).GetProperties().Select(property => property.Name).Order().ToArray());
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(snapshot), StringComparison.Ordinal);
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(signal), StringComparison.Ordinal);
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(await registration.Read()), StringComparison.Ordinal);
        Assert.Equal(new Dictionary<string, string> { ["RedirectPath"] = "/callback" }, snapshot.Settings);
        Assert.Equal(snapshot.Settings, (await registration.Read()).Settings);
    }

    [Fact]
    public async Task SeedingAfterAnOperatorClearedEveryFieldIsANoOp()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var google = Registration(brain, "google");
        await google.Configure(Values(("ClientId", Canary)));
        var cleared = await google.Clear("ClientId");

        var seeded = await google.SeedIfUnconfigured(Values(("ClientId", OtherCanary)));

        Assert.Equal(RegistrationStatus.Unconfigured, seeded.Status);
        Assert.Equal(cleared.Revision, seeded.Revision);
        Assert.Equal(RegistrationStatus.Unconfigured, (await google.Read()).Status);
    }

    [Fact]
    public async Task APartialConfigurationReportsMissingFieldNamesOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var registration = Registration(brain, "google");

        var snapshot = await registration.Configure(Values(("ClientId", Canary)));

        Assert.Equal(RegistrationStatus.Partial, snapshot.Status);
        Assert.Equal(["ClientSecret", "RedirectPath"], snapshot.MissingFields);
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(snapshot), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownFieldIsRejectedWithoutEchoingTheSubmittedValueOrStoringAnything()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var registration = Registration(brain, "google");

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => registration.Configure(Values(("ClientId", "fine"), ("NoSuchField", Canary))));

        Assert.DoesNotContain(Canary, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("NoSuchField", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(RegistrationStatus.Unconfigured, (await registration.Read()).Status);
    }

    [Fact]
    public async Task ARegistrationForAnUndeclaredIntegrationRefusesEveryCall()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Registration(brain, "nothing").Read());
    }

    [Fact]
    public async Task ReleaseReturnsValuesOnlyForATrustedCaller()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var registration = Registration(brain, "openai");
        await registration.Configure(Values(("ApiKey", Canary)));

        foreach (var refused in new[]
        {
            Caller(CallerKind.User, TrustedEdge.AuthenticatedHttp),
            Caller(CallerKind.Assistant, TrustedEdge.AuthenticatedHttp),
            Caller(CallerKind.App, TrustedEdge.AppProxy),
            Caller(CallerKind.Scheduler, TrustedEdge.Scheduler),
            Caller(CallerKind.Platform, TrustedEdge.AuthenticatedHttp),
            Caller(CallerKind.Platform, TrustedEdge.Platform) with { PrincipalId = "" },
        })
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => registration.Release(refused));
            Assert.DoesNotContain(Canary, error.ToString(), StringComparison.Ordinal);
        }

        var released = await registration.Release(Caller(CallerKind.Platform, TrustedEdge.Platform));
        Assert.Equal(Canary, released.Values["ApiKey"]);
        Assert.DoesNotContain(Canary, released.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReleaseRefusesAnIncompleteRegistrationEvenForATrustedCaller()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var registration = Registration(brain, "google");
        await registration.Configure(Values(("ClientId", Canary)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => registration.Release(Caller(CallerKind.Platform, TrustedEdge.Platform)));
    }

    [Fact]
    public async Task SeedingAppliesOnlyToAnUnconfiguredRegistration()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeded = new Dictionary<string, string?>
        {
            ["DigitalBrain:Integrations:google:ClientId"] = Canary + "-id",
            ["DigitalBrain:Integrations:google:ClientSecret"] = Canary,
            ["DigitalBrain:Integrations:google:RedirectPath"] = "/callback",
        };
        await using var brain = await StartAsync(ct, seeded);
        var google = Registration(brain, "google");
        Assert.Equal(RegistrationStatus.Ready, (await google.Read()).Status);
        Assert.Equal(RegistrationStatus.Unconfigured, (await Registration(brain, "openai").Read()).Status);

        await google.Clear("ClientId");
        await google.Configure(Values(("ClientSecret", OtherCanary)));
        var seeder = new RegistrationSeeder(
            new ConfigurationBuilder().AddInMemoryCollection(seeded).Build(),
            brain.SiloServices.GetRequiredService<IReadOnlyList<IntegrationDefinition>>(),
            brain.Grains(),
            NullLogger<RegistrationSeeder>.Instance);
        await seeder.SeedAsync(ct);

        var after = await google.Read();
        Assert.Equal(RegistrationStatus.Partial, after.Status);
        Assert.Equal(["ClientId"], after.MissingFields);
        await google.Configure(Values(("ClientId", "restored")));
        var released = await google.Release(Caller(CallerKind.Platform, TrustedEdge.Platform));
        Assert.Equal(OtherCanary, released.Values["ClientSecret"]);
    }

    [Fact]
    public async Task ClearingAFieldReturnsToPartialAndRemovesTheVaultEntry()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var registration = Registration(brain, "openai");
        await registration.Configure(Values(("ApiKey", Canary)));
        var vaultReference = SecretRef.For(IntegrationVault.Owner, IntegrationVault.SecretName("openai", "ApiKey"), "ApiKey", true);
        var platform = Caller(CallerKind.Platform, TrustedEdge.Platform) with { AppId = "test" };
        Assert.Equal(Canary, await brain.Get<ISecrets>(IntegrationVault.Owner).Resolve(platform, vaultReference, ct));

        var snapshot = await registration.Clear("ApiKey");

        Assert.Equal(RegistrationStatus.Unconfigured, snapshot.Status);
        Assert.Equal(["ApiKey"], snapshot.MissingFields);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => brain.Get<ISecrets>(IntegrationVault.Owner).Resolve(platform, vaultReference, ct));
    }

    [Fact]
    public async Task ClearingAPartiallyConfiguredRegistrationReportsPartial()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var registration = Registration(brain, "google");
        await registration.Configure(Values(("ClientId", "a"), ("ClientSecret", "b"), ("RedirectPath", "/c")));

        var snapshot = await registration.Clear("ClientSecret");

        Assert.Equal(RegistrationStatus.Partial, snapshot.Status);
        Assert.Equal(["ClientSecret"], snapshot.MissingFields);
    }

    [Fact]
    public async Task TheCatalogReportsEveryDefinitionWithItsStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        await Registration(brain, "google").Configure(Values(("ClientId", Canary)));
        await Registration(brain, "openai").Configure(Values(("ApiKey", Canary)));

        var entries = await IntegrationCatalog.ListAsync(
            brain.SiloServices.GetRequiredService<IReadOnlyList<IntegrationDefinition>>(), brain.Grains(), ct);

        Assert.Equal(["anthropic", "google", "openai"], entries.Select(entry => entry.Id).Order().ToArray());
        Assert.Equal("Partial", entries.Single(entry => entry.Id == "google").Status);
        Assert.Equal(["ClientSecret", "RedirectPath"], entries.Single(entry => entry.Id == "google").MissingFields);
        Assert.Equal("Ready", entries.Single(entry => entry.Id == "openai").Status);
        Assert.Equal("Unconfigured", entries.Single(entry => entry.Id == "anthropic").Status);
        Assert.Equal("Anthropic", entries.Single(entry => entry.Id == "anthropic").DisplayName);

        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            ["displayName", "id", "missingFields", "status"],
            document.RootElement[0].EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.DoesNotContain(Canary, json, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEndpointsAreRoutedWithTheCatalogOutsideAnyBrainScope()
    {
        var app = (IEndpointRouteBuilder)WebApplication.CreateBuilder().Build();
        app.MapIntegrations();

        var routes = app.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Select(endpoint => (
                Method: endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single(),
                Pattern: endpoint.RoutePattern.RawText!.TrimEnd('/')))
            .ToArray();

        Assert.Contains(("GET", "/integrations"), routes);
        Assert.Contains(("POST", "/integrations/{id}/registration"), routes);
        Assert.Contains(("DELETE", "/integrations/{id}/registration/{field}"), routes);
        Assert.All(routes, route => Assert.DoesNotContain("brainId", route.Pattern, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OnlyTheDeploymentOperatorMayWriteARegistration()
    {
        var basic = Config(("DigitalBrain:Auth:Username", "ops"), ("DigitalBrain:Auth:Password", "a-long-operator-password"));
        var open = Config();

        Assert.True(IntegrationOperatorGate.Allows(Http("ops"), basic));
        Assert.False(IntegrationOperatorGate.Allows(Http("someone-else"), basic));
        Assert.False(IntegrationOperatorGate.Allows(Http("owner"), basic));
        Assert.True(IntegrationOperatorGate.Allows(Http("owner"), open));
        Assert.False(IntegrationOperatorGate.Allows(Http("cookie-owner-of-another-account"), open));
        Assert.False(IntegrationOperatorGate.Allows(Http("ops") with { Kind = CallerKind.App, StampedBy = TrustedEdge.AppProxy }, basic));
        Assert.False(IntegrationOperatorGate.Allows(null, open));
    }

    [Fact]
    public async Task RegistrationWritesRefuseAnAppStampedAmbientCallerEvenWithAForgedPlatformParameter()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var registration = Registration(brain, "openai");
        await registration.Configure(Values(("ApiKey", Canary)));
        var script = Caller(CallerKind.App, TrustedEdge.AppProxy);

        Core.Enforcement.CallerContextStamper.Stamp(script);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => registration.Configure(Values(("ApiKey", OtherCanary))));
            await Assert.ThrowsAsync<InvalidOperationException>(() => registration.Clear("ApiKey"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => registration.SeedIfUnconfigured(Values(("ApiKey", OtherCanary))));
        }
        finally { Orleans.Runtime.RequestContext.Clear(); }

        var released = await registration.Release(Caller(CallerKind.Platform, TrustedEdge.Platform));
        Assert.Equal(Canary, released.Values["ApiKey"]);
    }

    [Fact]
    public async Task ReleaseSucceedsUnderAnAppStampedAmbientContextWhenTheParameterIsProperPlatform()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var registration = Registration(brain, "openai");
        await registration.Configure(Values(("ApiKey", Canary)));

        Core.Enforcement.CallerContextStamper.Stamp(Caller(CallerKind.App, TrustedEdge.AppProxy));
        try
        {
            var released = await registration.Release(Caller(CallerKind.Platform, TrustedEdge.Platform));
            Assert.Equal(Canary, released.Values["ApiKey"]);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => registration.Release(Caller(CallerKind.App, TrustedEdge.AppProxy)));
        }
        finally { Orleans.Runtime.RequestContext.Clear(); }
    }

    [Fact]
    public async Task SettingValuesAreVisibleInTheSnapshotAndClearingRemovesThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var registration = Registration(brain, "google");

        var configured = await registration.Configure(Values(("ClientId", Canary), ("RedirectPath", "/here")));

        Assert.Equal(new Dictionary<string, string> { ["RedirectPath"] = "/here" }, configured.Settings);
        Assert.DoesNotContain(Canary, JsonSerializer.Serialize(configured), StringComparison.Ordinal);
        Assert.Empty((await registration.Clear("RedirectPath")).Settings);
    }

    [Fact]
    public void TheVaultAndRegistrationContractsAreMarkedPlatformOnly()
    {
        Assert.True(PlatformOnlyAttribute.AppliesTo(typeof(ISecrets)));
        Assert.True(PlatformOnlyAttribute.AppliesTo(typeof(IIntegrationRegistration)));
    }

    [Fact]
    public void EveryPublicNeuronContractInThePlatformAssemblyIsPlatformOnlyByAssemblyIdentity()
    {
        var platform = typeof(IntegrationDiscovery).Assembly;

        Assert.True(PlatformAssemblyAttribute.IsPlatform(platform));
        Assert.False(PlatformAssemblyAttribute.IsPlatform(typeof(IntegrationDefinition).Assembly));
        Assert.All(platform.GetExportedTypes().Where(type => type.IsInterface && typeof(INeuron).IsAssignableFrom(type)),
            contract => Assert.True(PlatformOnlyAttribute.AppliesTo(contract)));
    }

    [Fact]
    public void EveryExportedNeuronContractInSdkIsEitherPlatformOnlyOrExplicitlyAllowed()
    {
        var sdk = typeof(ISecrets).Assembly;
        var scriptSafeAllowList = new[] { nameof(IIntegrationRegistration) };

        var exported = sdk.GetExportedTypes()
            .Where(type => type.IsInterface && typeof(INeuron).IsAssignableFrom(type))
            .ToArray();

        Assert.All(exported, contract =>
        {
            var isPlatformOnly = PlatformOnlyAttribute.AppliesTo(contract);
            var isAllowed = scriptSafeAllowList.Contains(contract.Name);
            Assert.True(isPlatformOnly || isAllowed,
                $"{contract.Name} must be either [PlatformOnly] (via PlatformOnlyAttribute.AppliesTo) or in the script-safe allow-list.");
        });
    }

    [Fact]
    public void TheVaultOwnerCannotBeAPrincipalId()
    {
        Assert.DoesNotMatch("^[a-z0-9][a-z0-9-]*$", IntegrationVault.Owner);
    }

    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value)).Build();

    private static CallerContext Http(string principal) => new()
    {
        PrincipalId = principal,
        AccountId = principal,
        BrainId = principal,
        Kind = CallerKind.User,
        StampedBy = TrustedEdge.AuthenticatedHttp,
    };

    [Fact]
    public void DefinitionsAreDiscoveredFromModuleTypesAndZeroDefinitionsIsFine()
    {
        Assert.Empty(IntegrationDiscovery.Collect([typeof(DigitalBrain.Platform.PlatformHosting)]));

        var found = IntegrationDiscovery.Collect([typeof(FakeGoogleModule), typeof(FakeAiModule), typeof(DigitalBrain.Platform.PlatformHosting)]);

        Assert.Equal(["anthropic", "google", "openai"], found.Select(definition => definition.Id).Order().ToArray());
        Assert.Throws<InvalidOperationException>(
            () => IntegrationDiscovery.Collect([typeof(FakeGoogleModule), typeof(FakeRivalGoogleModule)]));
    }

    [Fact]
    public void ADefinitionRejectsUnsafeOrDuplicateFieldNames()
    {
        var definition = IntegrationDefinition.For("x", "X").RequiresSecret("Key");
        Assert.Throws<ArgumentException>(() => definition.RequiresSetting("Key"));
        Assert.Throws<ArgumentException>(() => definition.RequiresSecret("has space"));
        Assert.Throws<ArgumentException>(() => definition.RequiresSecret("a:b"));
        Assert.Throws<ArgumentException>(() => IntegrationDefinition.For("Bad:Id", "X"));
    }

    private static Task<UnitBrain> StartAsync(CancellationToken cancellationToken, IReadOnlyDictionary<string, string?>? configuration = null)
        => UnitTest.Create()
            .WithExecution(new TestExecutionOptions { PrivateConfiguration = configuration ?? new Dictionary<string, string?>() })
            .WithModule<FakeGoogleModule>()
            .WithModule<FakeAiModule>()
            .StartAsync(cancellationToken);

    private static IIntegrationRegistration Registration(UnitBrain brain, string id) => brain.Get<IIntegrationRegistration>($"integration/{id}");

    private static ConfigureRegistration Values(params (string Field, string Value)[] values)
        => new() { Values = values.ToDictionary(pair => pair.Field, pair => pair.Value) };

    private static CallerContext Caller(CallerKind kind, TrustedEdge edge) => new()
    {
        PrincipalId = "someone",
        AccountId = "someone",
        BrainId = "someone",
        Kind = kind,
        StampedBy = edge,
        AppId = "integrations-test",
    };
}
