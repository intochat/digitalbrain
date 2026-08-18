using DigitalBrain.Abstractions;
using Orleans.Runtime;

namespace DigitalBrain.Core;

// The owner's living registry + graph + router: one grain, one snapshot state. Contexts are
// attention frames; touching a node (register / resolve-hit / route-hit) bumps its LastUsed
// and the scoped context's tally — plain counters, nothing more.
[GrainType("brain")]
internal sealed class BrainEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<BrainState> state)
    : Entity<BrainState>(state), IBrain
{
    private static readonly IReadOnlyDictionary<string, int> NoTallies =
        new Dictionary<string, int>(StringComparer.Ordinal);

    public async Task Register(BrainReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var snapshot = Snapshot();
        await SaveAsync(Touched(snapshot, reference, snapshot.ActiveContext, DateTimeOffset.UtcNow));
    }

    public async Task<BrainReference?> Resolve(string hint, string? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hint);

        var snapshot = Snapshot();
        var scopeName = context ?? snapshot.ActiveContext;
        var candidates = snapshot.Nodes.Where(node => Matches(hint, node)).ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        var scope = snapshot.Contexts.FirstOrDefault(c => IsNamed(c, scopeName));
        var winner = WinnerAmong(candidates, hint, scope);
        var now = DateTimeOffset.UtcNow;
        await SaveAsync(Touched(snapshot, winner, scopeName, now));
        return winner with { LastUsed = now };
    }

    public async Task UseContext(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var snapshot = Snapshot();
        var now = DateTimeOffset.UtcNow;
        var contexts = WithContext(snapshot.Contexts, name, snapshot.ActiveContext, now)
            .Select(c => IsNamed(c, name) ? c with { LastUsed = now } : c)
            .ToArray();
        await SaveAsync(snapshot with { Contexts = contexts, ActiveContext = name });
    }

    public Task<IReadOnlyList<BrainContext>> Contexts()
        => Task.FromResult(Snapshot().Contexts);

    public async Task Connect(Connection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var snapshot = Snapshot();
        if (snapshot.Connections.Contains(connection))
        {
            return;
        }

        await SaveAsync(snapshot with { Connections = [.. snapshot.Connections, connection] });
    }

    public async Task Disconnect(Connection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var snapshot = Snapshot();
        var kept = snapshot.Connections.Where(c => c != connection).ToArray();
        if (kept.Length == snapshot.Connections.Count)
        {
            return;
        }

        await SaveAsync(snapshot with { Connections = kept });
    }

    // Graph connections only; capability search stays the caller's fallback (SystemTools
    // already consults CapabilityIndex when nothing is connected).
    public async Task<Connection?> Route(string alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        var snapshot = Snapshot();
        var connection = snapshot.Connections.FirstOrDefault(
            c => string.Equals(c.Role, alias, StringComparison.OrdinalIgnoreCase));
        if (connection is null)
        {
            return null;
        }

        var target = snapshot.Nodes.FirstOrDefault(node =>
            node.Kind == BrainReferenceKind.Neuron
            && string.Equals(node.Type, connection.To.Type, StringComparison.Ordinal)
            && string.Equals(node.Name, connection.To.Name, StringComparison.Ordinal));
        if (target is not null)
        {
            await SaveAsync(Touched(snapshot, target, snapshot.ActiveContext, DateTimeOffset.UtcNow));
        }

        return connection;
    }

    private BrainState Snapshot()
        => State ?? new BrainState(
            Nodes: [],
            Connections: [],
            Contexts: [new BrainContext(BrainState.DefaultContext, [], DateTimeOffset.UtcNow, NoTallies)],
            ActiveContext: BrainState.DefaultContext);

    private static BrainState Touched(
        BrainState snapshot,
        BrainReference reference,
        string contextName,
        DateTimeOffset now)
    {
        var used = reference with { LastUsed = now };
        var nodes = snapshot.Nodes.Where(node => !SameNode(node, used)).Append(used).ToArray();
        var contexts = WithContext(snapshot.Contexts, contextName, snapshot.ActiveContext, now)
            .Select(c => IsNamed(c, contextName)
                ? c with
                {
                    Members = [.. c.Members.Where(member => !SameNode(member, used)), used],
                    LastUsed = now,
                    Tallies = Bumped(c.Tallies, used.Key),
                }
                : c)
            .ToArray();

        return snapshot with { Nodes = nodes, Contexts = contexts };
    }

    private static IReadOnlyList<BrainContext> WithContext(
        IReadOnlyList<BrainContext> contexts,
        string name,
        string activeContext,
        DateTimeOffset now)
    {
        if (contexts.Any(c => IsNamed(c, name)))
        {
            return contexts;
        }

        var kept = contexts.ToList();
        if (kept.Count >= BrainState.MaximumContexts)
        {
            kept.Remove(kept
                .Where(c => !IsNamed(c, activeContext))
                .OrderBy(c => c.LastUsed)
                .First());
        }

        kept.Add(new BrainContext(name, [], now, NoTallies));
        return kept;
    }

    private static BrainReference WinnerAmong(
        BrainReference[] candidates,
        string hint,
        BrainContext? scope)
    {
        if (scope is not null)
        {
            var scoped = candidates
                .Where(node => scope.Members.Any(member => SameNode(member, node)))
                .OrderByDescending(node => MatchesName(hint, node))
                .ThenByDescending(node => scope.Tallies.GetValueOrDefault(node.Key))
                .ThenByDescending(node => node.LastUsed)
                .FirstOrDefault();
            if (scoped is not null)
            {
                return scoped;
            }
        }

        return candidates
            .OrderByDescending(node => MatchesName(hint, node))
            .ThenByDescending(node => node.LastUsed)
            .First();
    }

    // A hint names either the node itself or its concept; grain types carry the concept plus
    // a suffix ("chart" -> "chartentity"), so the type match is a prefix match.
    private static bool Matches(string hint, BrainReference node)
        => MatchesName(hint, node)
            || node.Type.StartsWith(hint, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesName(string hint, BrainReference node)
        => string.Equals(node.Name, hint, StringComparison.OrdinalIgnoreCase);

    private static bool SameNode(BrainReference left, BrainReference right)
        => left.Kind == right.Kind
            && string.Equals(left.Type, right.Type, StringComparison.Ordinal)
            && string.Equals(left.Name, right.Name, StringComparison.Ordinal);

    private static bool IsNamed(BrainContext context, string name)
        => string.Equals(context.Name, name, StringComparison.Ordinal);

    private static IReadOnlyDictionary<string, int> Bumped(
        IReadOnlyDictionary<string, int> tallies,
        string key)
    {
        var bumped = tallies.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
        bumped[key] = bumped.GetValueOrDefault(key) + 1;
        return bumped;
    }
}
