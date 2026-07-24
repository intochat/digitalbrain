using System.ComponentModel;
using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class EdgeContracts(TestingFixture fixture)
{
    private const string OAuthClientId =
        "DigitalBrain:Salesforce:ClientId";

    private static readonly HashSet<string> ForbiddenTypes =
    [
        "Microsoft.Extensions.DependencyInjection.IServiceCollection",
        "Microsoft.Extensions.Configuration.IConfiguration",
        "Microsoft.Extensions.Configuration.IConfigurationBuilder",
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
        var configuration = current
            .Neuron<IConfigurationEdgeProbeNeuron>("stale-oauth");
        Assert.Null(await configuration.Reference.Read(OAuthClientId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RuntimeResolvesConfiguredChatAlias(bool firstAlias)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test =
            await fixture.CreateBrainAsync(cancellationToken);
        var value = firstAlias ? "first" : "second";
        var alias = firstAlias
            ? nameof(FirstChatEdgeProbeNeuron)
            : nameof(SecondChatEdgeProbeNeuron);

        var response = firstAlias
            ? await test
                .Neuron<IFirstChatEdgeProbeNeuron>(value)
                .Reference
                .Invoke(value)
            : await test
                .Neuron<ISecondChatEdgeProbeNeuron>(value)
                .Reference
                .Invoke(value);

        Assert.Equal($"chat:{alias}:{value}", response);
        Assert.Contains(
            $"{alias}:{value}",
            test.ProbeChat().Invocations);
    }

    [Fact]
    public async Task RuntimeResolvesUnkeyedMcpTransport()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test =
            await fixture.CreateBrainAsync(cancellationToken);
        var probe = test.Neuron<IMcpEdgeProbeNeuron>("mcp");

        var response = await probe.Reference.Invoke("tools/list");

        Assert.Equal("mcp:tools/list", response);
        Assert.Equal(
            ["tools/list"],
            test.ProbeMcp().Invocations);
    }

    [Fact]
    public async Task RuntimeConfigurationObservesOAuthChanges()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test =
            await fixture.CreateBrainAsync(cancellationToken);
        var probe = test
            .Neuron<IConfigurationEdgeProbeNeuron>("oauth-dynamic");

        test.SetOAuthParameter(OAuthClientId, "first-client");
        Assert.Equal(
            "first-client",
            await probe.Reference.Read(OAuthClientId));

        test.SetOAuthParameter(OAuthClientId, "replacement-client");
        Assert.Equal(
            "replacement-client",
            await probe.Reference.Read(OAuthClientId));
    }

    [Fact]
    public async Task RuntimeConfigurationClearsOAuthBeforeNextMethod()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var first =
            await fixture.CreateBrainAsync(cancellationToken))
        {
            first.SetOAuthParameter(OAuthClientId, "must not cross");
            var firstProbe = first
                .Neuron<IConfigurationEdgeProbeNeuron>("oauth-first");
            Assert.Equal(
                "must not cross",
                await firstProbe.Reference.Read(OAuthClientId));
        }

        await using var second =
            await fixture.CreateBrainAsync(cancellationToken);
        var secondProbe = second
            .Neuron<IConfigurationEdgeProbeNeuron>("oauth-second");

        Assert.Null(await secondProbe.Reference.Read(OAuthClientId));
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
                typeof(FirstChatEdgeProbeNeuron),
                typeof(SecondChatEdgeProbeNeuron),
            ],
            new ProbeChatAdapter(script),
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
            new ProbeChatAdapter(script),
            script,
            static value => value.Reset());

    internal static void ConfigureProbeMcp(
        this DigitalBrainTestBuilder builder,
        McpScriptProbe script)
        => builder.ConfigureSouthboundMcpTransport<
            IProbeMcpTransport,
            McpScriptProbe>(
            new ProbeMcpTransport(script),
            script,
            static value => value.Reset());

    internal static EdgeScriptProbe ProbeChat(this TestBrain brain)
        => brain.ChatClientScript<EdgeScriptProbe>();

    internal static McpScriptProbe ProbeMcp(this TestBrain brain)
        => brain.SouthboundMcpTransportScript<McpScriptProbe>();
}

internal interface IProbeChatAdapter
{
    string Invoke(string alias, string value);
}

internal sealed class ProbeChatAdapter(EdgeScriptProbe script) :
    IProbeChatAdapter
{
    public string Invoke(string alias, string value)
        => script.Invoke(alias, value);
}

internal interface IProbeMcpTransport
{
    string Invoke(string value);
}

internal sealed class ProbeMcpTransport(McpScriptProbe script) :
    IProbeMcpTransport
{
    public string Invoke(string value)
        => script.Invoke(value);
}

internal sealed class ProbeEdgeNeuron;

internal sealed class SecondProbeEdgeNeuron;

internal sealed class EdgeScriptProbe
{
    private readonly List<string> _invocations = [];
    private readonly List<string> _replies = [];

    internal IReadOnlyList<string> Invocations => _invocations;

    internal IReadOnlyList<string> Replies => _replies;

    internal int ResetCount { get; private set; }

    internal void Enqueue(string reply)
        => _replies.Add(reply);

    internal string Invoke(string alias, string value)
    {
        _invocations.Add($"{alias}:{value}");
        return $"chat:{alias}:{value}";
    }

    internal void Reset()
    {
        ResetCount++;
        _invocations.Clear();
        _replies.Clear();
    }
}

internal sealed class McpScriptProbe
{
    private readonly List<string> _invocations = [];

    internal IReadOnlyList<string> Invocations => _invocations;

    internal string Invoke(string value)
    {
        _invocations.Add(value);
        return $"mcp:{value}";
    }

    internal void Reset()
        => _invocations.Clear();
}

#pragma warning disable CA1515 // Public probe interfaces model an external consumer assembly.
[ClientEntryPoint]
public partial interface IFirstChatEdgeProbeNeuron : INeuron
{
    [Alias(nameof(Invoke))]
    Task<string> Invoke(string value);
}

[ClientEntryPoint]
public partial interface ISecondChatEdgeProbeNeuron : INeuron
{
    [Alias(nameof(Invoke))]
    Task<string> Invoke(string value);
}

[ClientEntryPoint]
public partial interface IMcpEdgeProbeNeuron : INeuron
{
    [Alias(nameof(Invoke))]
    Task<string> Invoke(string value);
}

[ClientEntryPoint]
public partial interface IConfigurationEdgeProbeNeuron : INeuron
{
    [Alias(nameof(Read))]
    Task<string?> Read(string name);
}
#pragma warning restore CA1515

internal sealed class FirstChatEdgeProbeNeuron :
    Neuron,
    IFirstChatEdgeProbeNeuron
{
    private readonly IProbeChatAdapter _chat;

    public FirstChatEdgeProbeNeuron()
        => _chat = ServiceProvider
            .GetRequiredKeyedService<IProbeChatAdapter>(
                typeof(FirstChatEdgeProbeNeuron));

    public Task<string> Invoke(string value)
        => Task.FromResult(_chat.Invoke(
            nameof(FirstChatEdgeProbeNeuron),
            value));
}

internal sealed class SecondChatEdgeProbeNeuron :
    Neuron,
    ISecondChatEdgeProbeNeuron
{
    private readonly IProbeChatAdapter _chat;

    public SecondChatEdgeProbeNeuron()
        => _chat = ServiceProvider
            .GetRequiredKeyedService<IProbeChatAdapter>(
                typeof(SecondChatEdgeProbeNeuron));

    public Task<string> Invoke(string value)
        => Task.FromResult(_chat.Invoke(
            nameof(SecondChatEdgeProbeNeuron),
            value));
}

internal sealed class McpEdgeProbeNeuron :
    Neuron,
    IMcpEdgeProbeNeuron
{
    private readonly IProbeMcpTransport _transport;

    public McpEdgeProbeNeuron()
        => _transport = ServiceProvider
            .GetRequiredService<IProbeMcpTransport>();

    public Task<string> Invoke(string value)
        => Task.FromResult(_transport.Invoke(value));
}

internal sealed class ConfigurationEdgeProbeNeuron :
    Neuron,
    IConfigurationEdgeProbeNeuron
{
    private readonly IConfiguration _configuration;

    public ConfigurationEdgeProbeNeuron()
        => _configuration = ServiceProvider
            .GetRequiredService<IConfiguration>();

    public Task<string?> Read(string name)
        => Task.FromResult(_configuration[name]);
}
