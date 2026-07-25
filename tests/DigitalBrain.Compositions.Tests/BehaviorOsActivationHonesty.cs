using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Flutter;
using DigitalBrain.Shell;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Compositions.Tests;

public sealed class BehaviorOsActivationHonesty(CompositionsFixture fixture)
{
    private const string ShellName = "desk";

    private static readonly Type[] ForbiddenBehaviorDispatchNames =
        new[]
        {
            typeof(OpenHome).Assembly,
            typeof(IDigitalBrain).Assembly,
            typeof(IShell).Assembly,
        }
        .SelectMany(static assembly => assembly.GetExportedTypes())
        .Where(static type =>
            type.Name is "IBehaviorTest" or "BehaviorRunner"
            || type.Name.Contains("BehaviorDispatch", StringComparison.Ordinal))
        .ToArray();

    [Fact(
        Explicit = true,
        DisplayName =
            "RESIDUAL dual product sentence: PostAuthBootstrap and OpenHome both open home today")]
    public async Task DualPathPostAuthBootstrapAndOpenHomeOpenSameHome()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(ShellName);

        await new OpenHome().RunAsync(test.Client, ShellName, cancellationToken);
        var fromOpenHome = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);

        await new PostAuthBootstrap().RunAsync(test.Client, ShellName, cancellationToken);
        var fromBootstrap = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);

        Assert.Equal(OpenHome.SceneKey, fromOpenHome.Synapse.SceneKey);
        Assert.Equal(OpenHome.SceneTitle, fromOpenHome.Synapse.Title);
        Assert.Equal(OpenHome.SceneKey, fromBootstrap.Synapse.SceneKey);
        Assert.Equal(OpenHome.SceneTitle, fromBootstrap.Synapse.Title);
        Assert.Equal(fromOpenHome.Synapse.SceneKey, fromBootstrap.Synapse.SceneKey);
        Assert.Equal(fromOpenHome.Synapse.Title, fromBootstrap.Synapse.Title);
    }

    [Fact(DisplayName =
        "activation product verb is IDigitalBrain.ActivateAsync — compositions remain pre-rail helpers, not host Program")]
    public void ActivationSynapseDrivesBootNotHostProgram()
    {
        Assert.Contains(
            typeof(IDigitalBrain).GetMethods(),
            method => method.Name == nameof(IDigitalBrain.ActivateAsync));
        AssertPublicSealedComposition(typeof(ActivateDigitalBrain));
        AssertPublicSealedComposition(typeof(BootOnActivation));
    }

    [Fact(DisplayName =
        "no Behavior-by-name dispatch API — IBehavior marker is synapse-activated, not Run(name)")]
    public void NoBehaviorByNameDispatchApi()
    {
        Assert.Empty(ForbiddenBehaviorDispatchNames);
        Assert.True(typeof(IBehavior).IsInterface);
        Assert.True(typeof(INeuron).IsAssignableFrom(typeof(IBehavior)));
    }

    private static void AssertPublicSealedComposition(Type composition)
    {
        Assert.Same(typeof(OpenHome).Assembly, composition.Assembly);
        Assert.True(
            composition is
            {
                IsClass: true,
                IsPublic: true,
                IsSealed: true,
                IsAbstract: false,
                IsNested: false,
            });
    }
}
