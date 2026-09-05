using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Microsoft;
using DigitalBrain.Microsoft.GitHub;
using DigitalBrain.Sdk;
using DigitalBrain.Sdk.Webhooks;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class GitHubRepositoryTests
{
    [Fact]
    public async Task Signed_webhook_is_durable_idempotent_and_rejects_conflicting_or_foreign_payloads()
    {
        await using var scenario = await GitHubScenario.StartAsync();
        var delivery = Guid.NewGuid().ToString();
        var request = scenario.Signed(delivery);
        Assert.Equal(WebhookAcceptance.Accepted, await scenario.Handler.HandleAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal(WebhookAcceptance.Duplicate, await scenario.Handler.HandleAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal(WebhookAcceptance.Conflict, await scenario.Handler.HandleAsync(scenario.Signed(delivery, number: 2), TestContext.Current.CancellationToken));
        Assert.Equal(WebhookAcceptance.Unauthorized, await scenario.Handler.HandleAsync(scenario.Signed(Guid.NewGuid().ToString(), repositoryId: 999), TestContext.Current.CancellationToken));
        var tampered = request with { Body = Encoding.UTF8.GetBytes("{}") };
        Assert.Equal(WebhookAcceptance.Unauthorized, await scenario.Handler.HandleAsync(tampered, TestContext.Current.CancellationToken));
        using var actor = VerifiedActor.Enter(scenario.Actor);
        var pending = Assert.Single(await scenario.Inbox.ReadPendingAsync());
        Assert.Equal(delivery, pending.DeliveryId);
        Assert.Equal(1, pending.PullRequestNumber);
        Assert.False(pending.Completed);
        Assert.Equal(0, scenario.Source.Reads);
    }

    [Fact]
    public async Task Receipt_acknowledges_while_repository_and_owner_root_are_waiting()
    {
        await using var scenario = await GitHubScenario.StartAsync();
        using var actor = VerifiedActor.Enter(scenario.Actor);
        scenario.Source.Block = true;
        var dispatch = scenario.DispatchAsync("busy");
        await scenario.Source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var queuedRead = scenario.Repository.RequestAsync(new ReadPullRequest(1), TestContext.Current.CancellationToken);
        var accepted = await scenario.Handler.HandleAsync(scenario.Signed(Guid.NewGuid().ToString()), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(WebhookAcceptance.Accepted, accepted);
        Assert.False(dispatch.IsCompleted);
        Assert.False(queuedRead.IsCompleted);
        scenario.Source.Release.TrySetResult();
        await dispatch;
        Assert.NotNull((await queuedRead).Snapshot);
    }

    [Fact]
    public async Task Repository_really_broadcasts_bound_facts_deduplicates_and_unsubscribes()
    {
        await using var scenario = await GitHubScenario.StartAsync();
        using var actor = VerifiedActor.Enter(scenario.Actor);
        var subscriber = scenario.Simulation.Brain.Get<IGitHubTestSubscriber>(scenario.Binding.InstanceName);
        await subscriber.SubscribeToAsync<IGitHubTestSubscriber, IRepository, PullRequestOpened>(scenario.Repository.Id, TestContext.Current.CancellationToken);
        await subscriber.SubscribeToAsync<IGitHubTestSubscriber, IRepository, PullRequestChecksChanged>(scenario.Repository.Id, TestContext.Current.CancellationToken);
        Assert.Contains(await scenario.Query(scenario.Repository.Id).ReadSynapses(), edge => edge.Target == subscriber.Id && edge.Kind == SynapseKind.Bound);
        await scenario.DispatchAsync("opened");
        await EventuallyAsync(async () => (await ReadAsync(subscriber)).Opened == 1);
        await scenario.DispatchAsync("opened");
        await scenario.DispatchAsync("another-delivery-same-semantic-event");
        Assert.Equal(1, (await ReadAsync(subscriber)).Opened);
        scenario.Source.Snapshot = scenario.Source.Snapshot with { CiRevision = "green" };
        await scenario.DispatchAsync("checks");
        await EventuallyAsync(async () => (await ReadAsync(subscriber)).Checks == 1);
        var incoming = await scenario.Query(subscriber.Id).ReadJournal(JournalKind.Incoming, 0);
        var fact = Assert.Single(incoming.Delta, item => item.Signal is PullRequestOpened);
        Assert.Equal(scenario.Repository.Id, fact.Caller);
        Assert.Equal(scenario.Actor.PrincipalId, fact.Principal);
        await subscriber.UnsubscribeFromAsync<IGitHubTestSubscriber, IRepository, PullRequestChecksChanged>(scenario.Repository.Id, TestContext.Current.CancellationToken);
        scenario.Source.Snapshot = scenario.Source.Snapshot with { CiRevision = "rerun" };
        await scenario.DispatchAsync("after-unsubscribe");
        await Task.Delay(TimeSpan.FromSeconds(6), TestContext.Current.CancellationToken);
        Assert.Equal(1, (await ReadAsync(subscriber)).Checks);
    }

    [Fact]
    public async Task Failed_first_subscriber_does_not_starve_healthy_recipient_and_outbox_retries_safely()
    {
        await using var scenario = await GitHubScenario.StartAsync();
        using var actor = VerifiedActor.Enter(scenario.Actor);
        var subscriber = scenario.Simulation.Brain.Get<IGitHubTestSubscriber>(scenario.Binding.InstanceName);
        var healthy = scenario.Simulation.Brain.Get<IGitHubTestSubscriber>(scenario.Binding.InstanceName + "-healthy");
        await subscriber.SendAsync(new SetGitHubFailure(true), TestContext.Current.CancellationToken);
        await subscriber.SubscribeToAsync<IGitHubTestSubscriber, IRepository, PullRequestOpened>(scenario.Repository.Id, TestContext.Current.CancellationToken);
        await healthy.SubscribeToAsync<IGitHubTestSubscriber, IRepository, PullRequestOpened>(scenario.Repository.Id, TestContext.Current.CancellationToken);
        Assert.Equal(subscriber.Id, (await scenario.Query(scenario.Repository.Id).ReadSynapses()).First(edge => edge.SignalType == nameof(PullRequestOpened)).Target);
        await scenario.DispatchAsync("opened");
        await EventuallyAsync(async () => (await scenario.Query(scenario.Repository.Id).ReadJournal(JournalKind.Outgoing, 0)).Delta.Any(item => item.Signal is PullRequestOpened));
        Assert.Equal(0, (await ReadAsync(subscriber)).Opened);
        await EventuallyAsync(async () => (await ReadAsync(healthy)).Opened == 1);
        await subscriber.SendAsync(new SetGitHubFailure(false), TestContext.Current.CancellationToken);
        await EventuallyAsync(async () => (await ReadAsync(subscriber)).Opened == 1);
        Assert.Equal(1, (await ReadAsync(healthy)).Opened);
    }

    [Fact]
    public async Task Microsoft_registers_github_without_an_aspire_project()
    {
        await using var scenario = await GitHubScenario.StartAsync(configuredModule: true);
        Assert.Equal(1, scenario.RegisteredWebhookSurfaces);
        Assert.Equal(WebhookAcceptance.Accepted, await scenario.Handler.HandleAsync(scenario.Signed(Guid.NewGuid().ToString()), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Repository_refuses_same_owner_subscriptions_from_another_principal()
    {
        await using var scenario = await GitHubScenario.StartAsync();
        var foreign = new ActorContext(PrincipalId.New(), "other-principal");
        var subscriber = scenario.Simulation.Brain.Get<IGitHubTestSubscriber>(PrincipalPartition.InstanceName(foreign.PrincipalId, "review"));
        using (VerifiedActor.Enter(foreign))
        {
            await Assert.ThrowsAsync<NeuronAuthorizationException>(() => subscriber.SubscribeToAsync<IGitHubTestSubscriber, IRepository, PullRequestOpened>(
                scenario.Repository.Id, TestContext.Current.CancellationToken));
        }
        using (VerifiedActor.Enter(scenario.Actor))
        {
            await Assert.ThrowsAsync<NeuronAuthorizationException>(() => subscriber.SubscribeToAsync<IGitHubTestSubscriber, IRepository, PullRequestOpened>(
                scenario.Repository.Id, TestContext.Current.CancellationToken));
        }
        Assert.DoesNotContain(await scenario.Query(scenario.Repository.Id).ReadSynapses(), edge => edge.Target == subscriber.Id);
    }

    [Fact]
    public async Task Configured_host_starts_then_dispatches_receipts_and_restores_durable_revocation()
    {
        await using var scenario = await GitHubScenario.StartAsync(configuredModule: true, runDispatcher: true);
        using var actor = VerifiedActor.Enter(scenario.Actor);
        await EventuallyAsync(() => Task.FromResult(scenario.Binding.RecoveryComplete));
        Assert.True(scenario.Binding.Enabled);
        Assert.Equal(WebhookAcceptance.Accepted, await scenario.Handler.HandleAsync(scenario.Signed(Guid.NewGuid().ToString()), TestContext.Current.CancellationToken));
        await EventuallyAsync(async () => (await scenario.Inbox.ReadPendingAsync(includeDeferred: true)).Length == 0);
        Assert.NotNull((await scenario.Repository.RequestAsync(new ReadPullRequest(1), TestContext.Current.CancellationToken)).Snapshot);
        var revoke = scenario.Signed(Guid.NewGuid().ToString(), eventName: "installation", action: "deleted");
        Assert.Equal(WebhookAcceptance.Accepted, await scenario.Handler.HandleAsync(revoke, TestContext.Current.CancellationToken));
        await EventuallyAsync(async () => (await scenario.Inbox.ReadPendingAsync(includeDeferred: true)).Length == 0);
        Assert.False(scenario.Binding.Enabled);

        // A fresh runtime binding must restore the persisted revocation before it grants
        // native tools/source access. Reusing the receipt grain simulates the durable seam.
        var fresh = new GitHubRepositoryBinding(scenario.Binding.Id, scenario.Binding.Owner, scenario.Binding.Principal,
            42, 43, 44, "owner", "repository", "fixture-private-key", "fixture-webhook-secret");
        fresh.BeginRecovery();
        using var lifetime = new GitHubStartedLifetime();
        using var worker = new GitHubWebhookDispatcher(new([fresh]), scenario.Simulation.Grains,
            NullLogger<GitHubWebhookDispatcher>.Instance, lifetime);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        await EventuallyAsync(() => Task.FromResult(fresh.RecoveryComplete));
        Assert.False(fresh.Enabled);
        await worker.StopAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("sha1=abc")]
    [InlineData("sha256=not-hex")]
    [InlineData(null)]
    public void Signature_validation_rejects_invalid_header_formats(string? signature)
        => Assert.False(GitHubWebhookHandler.ValidateSignature([1, 2, 3], signature, "test-secret-at-least-16"));

    private static Task<GitHubReceived> ReadAsync(NeuronReference<IGitHubTestSubscriber> subscriber)
        => subscriber.RequestAsync(new ReadGitHubReceived(), TestContext.Current.CancellationToken);

    private static async Task EventuallyAsync(Func<Task<bool>> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (!await predicate())
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "The durable GitHub delivery did not finish within its test budget.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }
}

internal sealed class GitHubScenario : IAsyncDisposable
{
    private GitHubScenario(BrainSimulation simulation, GitHubRepositoryBinding binding, GitHubFakeSource source, int surfaces)
    {
        Simulation = simulation; Binding = binding; Source = source; RegisteredWebhookSurfaces = surfaces;
        Actor = new(binding.Principal, "github-owner");
        Handler = new(binding, simulation.Grains);
    }
    internal BrainSimulation Simulation { get; }
    internal GitHubRepositoryBinding Binding { get; }
    internal GitHubFakeSource Source { get; }
    internal ActorContext Actor { get; }
    internal GitHubWebhookHandler Handler { get; }
    internal int RegisteredWebhookSurfaces { get; }
    internal NeuronReference<IRepository> Repository => Simulation.Brain.Get<IRepository>(Binding.InstanceName);
    internal IGitHubWebhookInbox Inbox => Simulation.Grains.GetGrain<IGitHubWebhookInbox>(Binding.Id);
    internal INeuronQuery Query(NeuronId id) => Simulation.Grains.GetGrain<INeuronQuery>(id.ToGrainId());

    internal static async Task<GitHubScenario> StartAsync(bool configuredModule = false, bool runDispatcher = false)
    {
        var binding = new GitHubRepositoryBinding("fixture", new OwnerId(DigitalBrainNames.DefaultOwner), PrincipalId.New(),
            42, 43, 44, "owner", "repository", "fixture-private-key", "fixture-webhook-secret");
        var source = new GitHubFakeSource();
        var configuration = new Dictionary<string, string?> { [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode };
        if (configuredModule)
        {
            var root = GitHubRepositoryBindings.ConfigurationRoot + ":fixture:";
            foreach (var entry in new Dictionary<string, string>
            {
                ["Owner"] = binding.Owner.Value, ["Principal"] = binding.Principal.Value.ToString(), ["RepositoryId"] = "42",
                ["InstallationId"] = "43", ["AppId"] = "44", ["RepoOwner"] = "owner", ["RepoName"] = "repository",
                ["PrivateKeyPem"] = "fixture-private-key", ["WebhookSecret"] = "fixture-webhook-secret",
            })
            {
                configuration[root + entry.Key] = entry.Value;
            }
        }
        var surfaces = 0;
        var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest([typeof(DigitalBrain.Execution.ExecutionModule), typeof(DigitalBrain.UI.UIModule), typeof(AIModule), typeof(MicrosoftModule)]),
            Configuration = configuration,
            ConfigureSilo = silo =>
            {
                surfaces = silo.Services.Count(item => item.ServiceType == typeof(IHttpSurface));
                foreach (var registration in silo.Services.Where(item => !runDispatcher && item.ImplementationType == typeof(GitHubWebhookDispatcher)).ToArray())
                {
                    silo.Services.Remove(registration);
                }
                if (runDispatcher)
                {
                    binding = ((GitHubRepositoryBindings)silo.Services.Last(item => item.ServiceType == typeof(GitHubRepositoryBindings)).ImplementationInstance!).Find("fixture")!;
                }
                else
                {
                    silo.Services.AddSingleton(new GitHubRepositoryBindings([binding]));
                }
                silo.Services.AddSingleton<IGitHubRepositorySource>(source);
            },
        });
        return new(simulation, binding, source, surfaces);
    }

    internal Task DispatchAsync(string delivery)
    {
        var dispatcher = new NeuronId("github-dispatcher", Binding.Owner, Binding.InstanceName);
        return Simulation.Grains.GetGrain<IGitHubRepositoryDispatcher>(dispatcher.ToGrainId()).DispatchAsync(Binding.Id,
            new(delivery, new string('0', 64), Binding.Revision, 1, false, DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);
    }

    internal WebhookRequest Signed(string delivery, int number = 1, long repositoryId = 42, string eventName = "pull_request", string action = "opened")
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            action, number,
            installation = new { id = 43 },
            repository = new { id = repositoryId, name = "repository", owner = new { login = "owner" } },
        });
        return new(body, new Dictionary<string, string[]>
        {
            ["X-GitHub-Delivery"] = [delivery], ["X-GitHub-Event"] = [eventName],
            ["X-Hub-Signature-256"] = ["sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Binding.WebhookSecret), body))],
        });
    }
    public ValueTask DisposeAsync() => Simulation.DisposeAsync();
}

internal sealed class GitHubFakeSource : IGitHubRepositorySource
{
    internal PullRequestSnapshot Snapshot { get; set; } = new(1, "Example PR", "https://github.com/owner/repository/pull/1", true, false,
        new string('a', 40), new string('b', 40), null, new string('a', 40), [], true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "revision", "pending", 42);
    internal int Reads;
    internal bool Block;
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<PullRequestSnapshot> GetPullRequestAsync(GitHubRepositoryBinding binding, int number, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Reads);
        binding.Authorize(binding.Owner, binding.Principal);
        Entered.TrySetResult();
        if (Block)
        {
            await Release.Task.WaitAsync(cancellationToken);
        }
        return Snapshot;
    }
    public Task<IReadOnlyList<PullRequestSnapshot>> ListOpenPullRequestsAsync(GitHubRepositoryBinding binding, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<PullRequestSnapshot>>([Snapshot]);
    public Task<GitHubReviewEvidence> GetReviewEvidenceAsync(GitHubRepositoryBinding binding, PullRequestSnapshot snapshot, CancellationToken cancellationToken)
        => throw new NotSupportedException();
}

internal sealed class GitHubStartedLifetime : IHostApplicationLifetime, IDisposable
{
    private readonly CancellationTokenSource _started = new();
    internal GitHubStartedLifetime() => _started.Cancel();
    public CancellationToken ApplicationStarted => _started.Token;
    public CancellationToken ApplicationStopping => CancellationToken.None;
    public CancellationToken ApplicationStopped => CancellationToken.None;
    public void StopApplication() { }
    public void Dispose() => _started.Dispose();
}

[Alias("github-test-subscriber")]
public interface IGitHubTestSubscriber : INeuron, IHandle<PullRequestOpened>, IHandle<PullRequestChecksChanged>, IHandle<ReadGitHubReceived>, IHandle<SetGitHubFailure>;
[GenerateSerializer, Alias("github-test.read")]
public sealed record ReadGitHubReceived : Signal<GitHubReceived>;
[GenerateSerializer, Alias("github-test.received")]
public sealed record GitHubReceived([property: Id(0)] int Opened, [property: Id(1)] int Checks) : Signal;
[GenerateSerializer, Alias("github-test.failure")]
public sealed record SetGitHubFailure([property: Id(0)] bool Fail) : Signal;
[GrainType("githubtestsubscriber")]
internal sealed class GitHubTestSubscriber(NeuronRuntime runtime) : Neuron(runtime), IGitHubTestSubscriber
{
    private readonly HashSet<string> _received = [];
    private int _opened;
    private int _checks;
    private bool _fail;
    public Task HandleAsync(PullRequestOpened signal, CancellationToken cancellationToken)
    {
        if (_fail)
        {
            throw new InvalidOperationException("fixture subscriber unavailable");
        }
        if (_received.Add(signal.EventId))
        {
            _opened++;
        }
        return Task.CompletedTask;
    }
    public Task HandleAsync(PullRequestChecksChanged signal, CancellationToken cancellationToken)
    {
        if (_received.Add(signal.EventId))
        {
            _checks++;
        }
        return Task.CompletedTask;
    }
    public Task HandleAsync(ReadGitHubReceived signal, CancellationToken cancellationToken) => ReplyAsync(new GitHubReceived(_opened, _checks));
    public Task HandleAsync(SetGitHubFailure signal, CancellationToken cancellationToken)
    {
        _fail = signal.Fail;
        return Task.CompletedTask;
    }
}
