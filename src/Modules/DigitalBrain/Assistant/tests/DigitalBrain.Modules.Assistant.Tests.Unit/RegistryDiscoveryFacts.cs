using System.Runtime.CompilerServices;
using DigitalBrain.AI.Agents;
using DigitalBrain.Apps;
using DigitalBrain.Assistant;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Kernel;
using DigitalBrain.Registry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Assistant.Tests.Unit;

public sealed class RegistryDiscoveryFacts
{
    [Fact]
    public async Task MissingRegistryIsAnExplicitDiscoveryFailureNotAnUnavailableGrainCall()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        CallerContextStamper.Stamp(new CallerContext
        {
            PrincipalId = "alice", AccountId = "alice", BrainId = "personal",
            Kind = CallerKind.User, StampedBy = TrustedEdge.AuthenticatedHttp,
        });
        try
        {
            var factory = new RegistryDiscoveryTools(brain.Grains, brain.SiloServices.GetRequiredService<ModuleInventory>());
            var tool = Assert.Single(factory.Create(() => new(BrainScope.CurrentId(), "run", "call")));
            var result = Assert.IsType<AgentToolOffer>(await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["query"] = "tables" }), ct));
            Assert.Empty(result.Tools);
            Assert.Contains("discovery_unavailable", System.Text.Json.JsonSerializer.Serialize(result.Result));
        }
        finally { Orleans.Runtime.RequestContext.Remove(CallerContextStamper.RequestContextKey); }
    }

    [Fact]
    public async Task MisspelledDatabaseRequestDiscoversToolsAlongsideAnInstalledApp()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = new DatabaseFixture();
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().WithModule<RegistryModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IRegistryResourceProvider>(database)).StartAsync(ct);
        CallerContextStamper.Stamp(new CallerContext
        {
            PrincipalId = "alice", AccountId = "alice", BrainId = "personal",
            Kind = CallerKind.User, StampedBy = TrustedEdge.AuthenticatedHttp,
        });
        try
        {
            var scope = BrainScope.CurrentId();
            var packageId = PackageId.Create("alice", "researcher");
            var revision = await brain.Get<IPackage>(packageId.ToString()).Commit(new(Guid.NewGuid(), null,
                new PackageContent(new("Researcher", "Research", [new("research", "Research")], [], Runtime: "prompt"), ""), "Initial"));
            await brain.Get<IApp>(scope + "/packages/" + packageId).Install(new(Guid.NewGuid(), new(packageId, revision.Id), new Dictionary<string, string>()));

            using var services = new ServiceCollection()
                .AddSingleton<IChatClient>(new DiscoveryModel())
                .AddSingleton<IAgentToolFactory>(new RegistryDiscoveryTools(brain.Grains, brain.SiloServices.GetRequiredService<ModuleInventory>()))
                .AddSingleton<IAgentToolFactory>(database)
                .AddSingleton(brain.SiloServices.GetRequiredService<IAgentToolSource>()).BuildServiceProvider();
            var events = new List<AgentTurnEvent>();
            await foreach (var item in new AgentTurnRunner(services).RunAsync(new("assistant", "run", scope, [],
                "show me data from postges, wich tables are there?", null, ToolNames: ["discover_capabilities"]), ct))
            { events.Add(item); }

            Assert.Empty(events.OfType<AgentTurnEvent.Failed>());
            Assert.Equal(["discover_capabilities", "postgres_schema"], events.OfType<AgentTurnEvent.ToolCompleted>().Select(item => item.Name));
            Assert.Contains(events.OfType<AgentTurnEvent.Text>(), item => item.Content == "research_results");
            Assert.Equal(scope, database.ReadScope);
        }
        finally { Orleans.Runtime.RequestContext.Remove(CallerContextStamper.RequestContextKey); }
    }

    private sealed class DatabaseFixture : IRegistryResourceProvider, IAgentToolFactory
    {
        public string Id => "postgres";
        public string? ReadScope;
        public Task<RegistryDiscovery> Discover(CancellationToken ct)
            => Task.FromResult(new RegistryDiscovery([new("postgres", "PostgreSQL tables", ["postgres_schema"])], []));
        public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
            => [AIFunctionFactory.Create(() => { ReadScope = context().ScopeId; return new { tables = new[] { "research_results" } }; }, "postgres_schema")];
    }

    private sealed class DiscoveryModel : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        {
            var results = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToArray();
            var next = results.Length == 0 ? "discover_capabilities" : "postgres_schema";
            if (results.Length >= 2) { return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "research_results"))); }
            Assert.Contains(options!.Tools!, tool => tool.Name == next);
            if (results.Length == 1) { Assert.Contains(options.Tools!, tool => tool.Name.StartsWith("app_", StringComparison.Ordinal)); }
            Dictionary<string, object?> arguments = results.Length == 0 ? new() { ["query"] = "postges tables" } : [];
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-" + results.Length, next, arguments)])));
        }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates()) { yield return update; }
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
