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

        // A self-route dispatches in place (Neuron delivers to itself without a grain call)
        // and recurses with no timeout at all.
        if (connection.From == connection.To)
        {
            throw new NeuronAuthorizationException(
                $"The brain refuses the self-wire {connection.From} --{connection.Role}--> "
                + $"{connection.To}.");
        }

        // The owner wall: the Connection payload is caller-supplied, so a foreign-owner
        // endpoint here would let one owner's brain route deliveries into another owner.
        var owner = GrainOwnership.RequireOwner(this.GetGrainId());
        if (connection.From.Owner != owner || connection.To.Owner != owner)
        {
            throw new NeuronAuthorizationException(
                $"The brain of owner '{owner}' cannot wire {connection.From} "
                + $"--{connection.Role}--> {connection.To}: both endpoints must belong to "
                + "this owner.");
        }

        var snapshot = Snapshot();
        if (snapshot.Connections.Any(c => SameWire(c, connection)))
        {
            return;
        }

        if (snapshot.Connections.Count >= BrainState.MaximumConnections)
        {
            throw new NeuronAuthorizationException(
                $"The brain holds its maximum of {BrainState.MaximumConnections} connections "
                + "and refuses another wire. Disconnect one first; wires are never evicted.");
        }

        // Routing is single-target: an emission's (source, alias) pair resolves at most one
        // receiver, so a second wire on the pair is refused instead of silently never firing.
        if (snapshot.Connections.FirstOrDefault(
                c => c.From == connection.From && SameRole(c.Role, connection.Role)) is { } occupied)
        {
            throw new NeuronAuthorizationException(
                $"The brain already routes '{connection.Role}' from {connection.From} to "
                + $"{occupied.To}. Disconnect that wire first; routing is single-target.");
        }

        // A routed cycle deadlocks its non-reentrant neurons until the Deliver timeout.
        if (CyclePath(snapshot.Connections, connection) is { } cycle)
        {
            throw new NeuronAuthorizationException(
                $"The brain refuses the wire {connection.From} --{connection.Role}--> "
                + $"{connection.To}: it closes the cycle {cycle}.");
        }

        await SaveAsync(snapshot with { Connections = [.. snapshot.Connections, connection] });
    }

    public async Task Disconnect(Connection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var snapshot = Snapshot();
        var kept = snapshot.Connections.Where(c => !SameWire(c, connection)).ToArray();
        if (kept.Length == snapshot.Connections.Count)
        {
            return;
        }

        await SaveAsync(snapshot with { Connections = kept });
    }

    public Task<IReadOnlyList<Connection>> Connections(NeuronId from, string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        return Task.FromResult<IReadOnlyList<Connection>>(
            [.. Snapshot().Connections.Where(c => c.From == from && SameRole(c.Role, role))]);
    }

    // Graph connections only; capability search stays the caller's fallback (SystemTools
    // already consults CapabilityIndex when nothing is connected). Connect keeps the
    // (source, role) pair unique, so the first match is the only match.
    public async Task<Connection?> Route(NeuronId source, string alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        var snapshot = Snapshot();
        var connection = snapshot.Connections.FirstOrDefault(
            c => c.From == source && SameRole(c.Role, alias));
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

        return Capped(snapshot with { Nodes = nodes, Contexts = contexts });
    }

    // Growth backstop mirroring the contexts cap: an evicted node also leaves every context's
    // Members and Tallies so nothing dangles.
    private static BrainState Capped(BrainState grown)
    {
        if (grown.Nodes.Count <= BrainState.MaximumNodes)
        {
            return grown;
        }

        var evicted = grown.Nodes
            .OrderBy(node => node.LastUsed)
            .Take(grown.Nodes.Count - BrainState.MaximumNodes)
            .ToArray();
        var contexts = grown.Contexts
            .Select(c => c with
            {
                Members = [.. c.Members.Where(member => !evicted.Any(e => SameNode(e, member)))],
                Tallies = c.Tallies
                    .Where(tally => !evicted.Any(e => string.Equals(e.Key, tally.Key, StringComparison.Ordinal)))
                    .ToDictionary(tally => tally.Key, tally => tally.Value, StringComparer.Ordinal),
            })
            .ToArray();

        return grown with
        {
            Nodes = [.. grown.Nodes.Where(node => !evicted.Any(e => SameNode(e, node)))],
            Contexts = contexts,
        };
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

    // Walks the existing table from the proposed target back toward the proposed source,
    // role-blind: a routed cycle deadlocks regardless of which aliases ride it. Returns the
    // closed path for the refusal message, or null when the wire is acyclic. Iterative on an
    // explicit stack: recursion depth would scale with the table and overflow untrappably.
    private static string? CyclePath(IReadOnlyList<Connection> table, Connection proposed)
    {
        List<Connection> path = [proposed];
        HashSet<NeuronId> visited = [proposed.To];
        // A frame is a node being expanded plus the table index its edge scan resumes from.
        var frames = new Stack<(NeuronId Node, int NextIndex)>();
        frames.Push((proposed.To, 0));

        while (frames.Count > 0)
        {
            var (node, index) = frames.Pop();
            while (index < table.Count && table[index].From != node)
            {
                index++;
            }

            if (index == table.Count)
            {
                path.RemoveAt(path.Count - 1);
                continue;
            }

            var edge = table[index];
            frames.Push((node, index + 1));
            path.Add(edge);

            if (edge.To == proposed.From)
            {
                return proposed.From + string.Concat(path.Select(e => $" --{e.Role}--> {e.To}"));
            }

            if (visited.Add(edge.To))
            {
                frames.Push((edge.To, 0));
            }
            else
            {
                path.RemoveAt(path.Count - 1);
            }
        }

        return null;
    }

    private static bool SameWire(Connection left, Connection right)
        => left.From == right.From
            && left.To == right.To
            && SameRole(left.Role, right.Role);

    private static bool SameRole(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

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
