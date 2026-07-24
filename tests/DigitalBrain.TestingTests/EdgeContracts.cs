using System.ComponentModel;
using System.Reflection;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class EdgeContracts(TestingFixture fixture)
{
    private static readonly HashSet<string> ForbiddenTypes =
    [
        "Microsoft.Extensions.DependencyInjection.IServiceCollection",
        "System.IServiceProvider",
        "Orleans.Hosting.ISiloBuilder",
        "Microsoft.Extensions.Hosting.IHostBuilder",
    ];

    [Fact]
    public void ExternalEdgeCatalogIsInternalAndClosed()
    {
        var assembly = typeof(DigitalBrainTestBuilder).Assembly;
        Assert.DoesNotContain(
            assembly.GetExportedTypes(),
            type => type.Name == "TestingEdges");

        var edgeKind = assembly.GetType(
            "DigitalBrain.Testing.TestEdgeKind",
            throwOnError: false);

        Assert.NotNull(edgeKind);
        Assert.True(edgeKind.IsEnum);
        Assert.False(edgeKind.IsPublic);
        Assert.Equal(
            [
                "ChatClient",
                "SouthboundMcpTransport",
                "OAuthParameters",
                "TimeProvider",
            ],
            Enum.GetNames(edgeKind));
    }

    [Fact]
    public void EdgeBridgesExposeNoContainerOrHostMutation()
    {
        var leaked = new[]
        {
            typeof(DigitalBrainTestBuilder),
            typeof(TestBrain),
        }
            .SelectMany(type => type
            .GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
            .Where(member => member is MethodInfo or PropertyInfo)
            .SelectMany(ReferencedTypes))
            .SelectMany(Expand)
            .Where(type => ForbiddenTypes.Contains(type.FullName ?? string.Empty))
            .ToArray();

        Assert.Empty(leaked);
    }

    [Fact]
    public void TestingSurfaceHasNoGenericSubstituteEscape()
    {
        var substitutes = typeof(DigitalBrainTestBuilder).Assembly
            .GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public))
            .Where(method => method.Name.StartsWith(
                "Substitute",
                StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(substitutes);
    }

    [Fact]
    public void ClosedEdgeBridgesAreEditorHiddenAndEdgeSpecific()
    {
        AssertEditorHidden(
            typeof(DigitalBrainTestBuilder),
            "ConfigureChatClient");
        AssertEditorHidden(
            typeof(DigitalBrainTestBuilder),
            "ConfigureSouthboundMcpTransport");
        AssertEditorHidden(
            typeof(TestBrain),
            "ChatClientScript");
        AssertEditorHidden(
            typeof(TestBrain),
            "SouthboundMcpTransportScript");
        AssertEditorHidden(
            typeof(TestBrain),
            "SetOAuthParameter");
        AssertEditorHidden(
            typeof(TestBrain),
            "OAuthParameter");

        Assert.DoesNotContain(
            typeof(DigitalBrainTestBuilder).GetMethods(),
            method => method.Name.Contains(
                "Edge",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(TestBrain).GetMethods(),
            method => method.Name.Contains(
                "Edge",
                StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateAssemblyAdapterForAClosedKindIsRejected()
    {
        var builder = new DigitalBrainTestBuilder();
        builder.ConfigureProbeChat(new EdgeScriptProbe());

        var failure = Assert.Throws<InvalidOperationException>(
            () => builder.ConfigureProbeChat(new EdgeScriptProbe()));

        Assert.Equal(
            "The 'ChatClient' test edge already has an assembly-configured adapter.",
            failure.Message);
    }

    [Fact]
    public void ChatAdapterAliasesMustBeNonEmptyDistinctConcreteClasses()
    {
        var emptyBuilder = new DigitalBrainTestBuilder();
        var empty = Assert.Throws<ArgumentException>(
            () => emptyBuilder.ConfigureProbeChatAliases(
                new EdgeScriptProbe()));
        Assert.Equal("neuronAliases", empty.ParamName);

        var duplicateBuilder = new DigitalBrainTestBuilder();
        var duplicate = Assert.Throws<ArgumentException>(
            () => duplicateBuilder.ConfigureProbeChatAliases(
                new EdgeScriptProbe(),
                typeof(ProbeEdgeNeuron),
                typeof(ProbeEdgeNeuron)));
        Assert.Equal("neuronAliases", duplicate.ParamName);

        var interfaceBuilder = new DigitalBrainTestBuilder();
        var notConcrete = Assert.Throws<ArgumentException>(
            () => interfaceBuilder.ConfigureProbeChatAliases(
                new EdgeScriptProbe(),
                typeof(IProbeChatAdapter)));
        Assert.Equal("neuronAliases", notConcrete.ParamName);
    }

    [Fact]
    public async Task MethodScopeResetClearsThePriorBrainScript()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = await fixture.CreateBrainAsync(cancellationToken);
        var firstScript = first.ProbeChat();
        firstScript.Enqueue("must not cross the method boundary");
        first.SetOAuthParameter("client-id", "must not cross");
        Assert.Single(firstScript.Replies);
        Assert.Equal(
            "must not cross",
            first.OAuthParameter("client-id"));
        await first.DisposeAsync();

        await using var second =
            await fixture.CreateBrainAsync(cancellationToken);
        var secondScript = second.ProbeChat();

        Assert.Same(firstScript, secondScript);
        Assert.Empty(secondScript.Replies);
        Assert.Null(second.OAuthParameter("client-id"));
        Assert.True(secondScript.ResetCount >= 2);
    }

    [Fact]
    public async Task DisposedBrainCannotReachTheNextMethodEdgeScope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stale = await fixture.CreateBrainAsync(cancellationToken);
        _ = stale.ProbeChat();
        stale.SetOAuthParameter("client-id", "stale");
        await stale.DisposeAsync();

        await using var current =
            await fixture.CreateBrainAsync(cancellationToken);

        Assert.Throws<ObjectDisposedException>(
            () => stale.ProbeChat());
        Assert.Throws<ObjectDisposedException>(
            () => stale.SetOAuthParameter("client-id", "crossed"));
        Assert.Throws<ObjectDisposedException>(
            () => stale.OAuthParameter("client-id"));
        Assert.Null(current.OAuthParameter("client-id"));
    }

    private static void AssertEditorHidden(
        Type declaringType,
        string methodName)
    {
        var methods = declaringType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == methodName)
            .ToArray();

        Assert.NotEmpty(methods);
        Assert.All(
            methods,
            method => Assert.Equal(
                EditorBrowsableState.Never,
                method.GetCustomAttribute<EditorBrowsableAttribute>()?.State));
    }

    private static IEnumerable<Type> ReferencedTypes(MemberInfo member)
        => member switch
        {
            MethodInfo method => method.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType),
            PropertyInfo property => [property.PropertyType],
            _ => [],
        };

    private static IEnumerable<Type> Expand(Type type)
    {
        yield return type;

        if (type.HasElementType)
        {
            foreach (var nested in Expand(type.GetElementType()!))
            {
                yield return nested;
            }
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nested in Expand(argument))
            {
                yield return nested;
            }
        }
    }
}

internal static class ProbeEdgeExtensions
{
    internal static void ConfigureProbeChat(
        this DigitalBrainTestBuilder builder,
        EdgeScriptProbe script)
        => builder.ConfigureChatClient<
            IProbeChatAdapter,
            EdgeScriptProbe>(
            [
                typeof(ProbeEdgeNeuron),
                typeof(SecondProbeEdgeNeuron),
            ],
            new ProbeChatAdapter(),
            script,
            static value => value.Reset());

    internal static void ConfigureProbeChatAliases(
        this DigitalBrainTestBuilder builder,
        EdgeScriptProbe script,
        params Type[] neuronAliases)
        => builder.ConfigureChatClient<
            IProbeChatAdapter,
            EdgeScriptProbe>(
            neuronAliases,
            new ProbeChatAdapter(),
            script,
            static value => value.Reset());

    internal static EdgeScriptProbe ProbeChat(this TestBrain brain)
        => brain.ChatClientScript<EdgeScriptProbe>();
}

internal interface IProbeChatAdapter;

internal sealed class ProbeChatAdapter : IProbeChatAdapter;

internal sealed class ProbeEdgeNeuron;

internal sealed class SecondProbeEdgeNeuron;

internal sealed class EdgeScriptProbe
{
    private readonly List<string> _replies = [];

    internal IReadOnlyList<string> Replies => _replies;

    internal int ResetCount { get; private set; }

    internal void Enqueue(string reply)
        => _replies.Add(reply);

    internal void Reset()
    {
        ResetCount++;
        _replies.Clear();
    }
}
