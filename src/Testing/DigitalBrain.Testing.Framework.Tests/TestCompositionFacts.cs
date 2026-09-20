using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Supabase;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Unit;
using DigitalBrain.Testing.E2E;

namespace DigitalBrain.Tests;

public sealed class TestCompositionFacts
{
    [Fact]
    public async Task BuilderForwardsBrowserOptionsBeforeAcquiringResources()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => E2ETest.For<TestCompositionFacts>()
            .WithBrowser(new() { SlowMoMilliseconds = -1 }).StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnitBuilderPreservesExecutionBudgetsAndStartsOnlyOnce()
    {
        var execution = new TestExecutionOptions { AssertionTimeout = TimeSpan.FromMilliseconds(120), CleanupTimeout = TimeSpan.FromSeconds(20) };
        var builder = UnitTest.Create().WithExecution(execution).WithModule<CompositionBuilderFacts.OtherModule>();
        await using var brain = await builder.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(execution, ((ITrackedBrain)brain).Execution);
        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void HostedOverridesRejectLocalSubstitutionAndOversizedPayloads()
    {
        var overrides = new CompositionOverrides().ConfigureModule<FlutterModule>(m => m.ConfigureLocalServices(_ => { }));
        Assert.Throws<NotSupportedException>(overrides.Serialize);
        Assert.Throws<ArgumentException>(() => new BrainCompositionBuilder().ApplyOverrides(new string('x', 32769)));
    }

    [Fact]
    public void UnknownAndCredentialMembersAreRejectedAtomically()
    {
        var application = new BrainCompositionBuilder().WithModule<FlutterModule>();
        var id = typeof(FlutterModule).FullName;
        var envelope = System.Text.Json.JsonSerializer.Serialize(new
        {
            Version = 1,
            Modules = new[] { new { Id = id, Patch = "{\"Hosting.Kind\":2,\"ApiKey\":\"private\"}" } },
        });
        Assert.Throws<ArgumentException>(() => application.ApplyOverrides(envelope));
        Assert.Equal("Window", Assert.Single(application.Build().Modules).Configuration["DigitalBrain:Flutter:Hosting:Kind"]);
    }

    [Fact]
    public void OverridePreservesUnassignedApplicationFieldsAndExplicitDefault()
    {
        var application = new BrainCompositionBuilder().WithModule<FlutterModule>(m => m.WithOptions(new()
        {
            Hosting = new() { Kind = FlutterHostKind.Web, ShellName = "application-workspace" },
        }));
        var calls = 0;
        var overrides = new CompositionOverrides().ConfigureModule<FlutterModule>(m => { calls++; m.WithWindowHost(); });
        application.ApplyOverrides(overrides.Serialize());
        var module = Assert.Single(application.Build().Modules);
        Assert.Equal("Window", module.Configuration["DigitalBrain:Flutter:Hosting:Kind"]);
        Assert.Equal("application-workspace", module.Configuration["DigitalBrain:Flutter:Hosting:ShellName"]);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void UnknownTargetCannotAddAModuleToApplication()
    {
        var application = new BrainCompositionBuilder().WithModule<FlutterModule>();
        var json = new CompositionOverrides().ConfigureModule<SupabaseModule>(m => m.WithPostgres()).Serialize();
        Assert.Throws<ArgumentException>(() => application.ApplyOverrides(json));
        Assert.Single(application.Build().Modules);
    }

    [Fact]
    public void WholeOptionsReplacementResetsUnspecifiedApplicationValues()
    {
        var application = new BrainCompositionBuilder().WithModule<FlutterModule>(m => m.WithOptions(new()
        {
            Hosting = new() { Kind = FlutterHostKind.Web, ShellName = "custom" },
        }));
        var json = new CompositionOverrides().ConfigureModule<FlutterModule>(m => m.WithOptions(new())).Serialize();
        application.ApplyOverrides(json);
        Assert.Equal("desk", Assert.Single(application.Build().Modules).Configuration["DigitalBrain:Flutter:Hosting:ShellName"]);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"Version\":99,\"Modules\":[]}")]
    public void InvalidEnvelopeIsRejectedWithoutEchoingPayload(string payload)
    {
        var application = new BrainCompositionBuilder().WithModule<FlutterModule>();
        var error = Assert.Throws<ArgumentException>(() => application.ApplyOverrides(payload));
        Assert.DoesNotContain(payload, error.Message);
    }
}
