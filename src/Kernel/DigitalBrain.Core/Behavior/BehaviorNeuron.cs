using System.Text;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Core;

// Durable repo-review runs: open repo → N file stances → moderator rounds → plan.
[GrainType(IBehavior.GrainTypeName)]
public sealed class BehaviorNeuron : Neuron, IBehavior
{
    private const string StateName = "behavior.state";

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<BehaviorState> _states;

    public BehaviorNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<BehaviorState>>();
    }

    public async Task HandleAsync(StartRepoReview synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        if (string.IsNullOrWhiteSpace(synapse.RootPath) || string.IsNullOrWhiteSpace(synapse.Intent))
        {
            throw new NeuronAuthorizationException("StartRepoReview requires RootPath and Intent.");
        }

        var maxFiles = synapse.MaxFiles <= 0 ? 30 : Math.Min(synapse.MaxFiles, 60);
        var rounds = synapse.ModeratorRounds <= 0 ? 3 : Math.Min(synapse.ModeratorRounds, 5);

        // Open + list via local FS (same rules as RepositoryNeuron) so the run is self-contained.
        var root = Path.GetFullPath(synapse.RootPath.Trim());
        if (!Directory.Exists(root))
        {
            throw new NeuronAuthorizationException($"Root '{root}' does not exist.");
        }

        var paths = ListFiles(root, maxFiles);
        var stances = new List<FileStance>(paths.Length);
        foreach (var relative in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            stances.Add(StanceFor(root, relative, synapse.Intent));
        }

        var moderator = Fold(stances, synapse.Intent, rounds);
        var plan = WritePlan(synapse.Intent, root, stances, moderator);
        var runId = Guid.NewGuid().ToString("N");
        var started = TimeProvider.GetUtcNow();
        var summary = new BehaviorRunSummary(
            runId,
            Status: "Completed",
            root,
            synapse.Intent.Trim(),
            paths.Length,
            stances.Count,
            moderator.Length,
            started,
            CompletedAt: TimeProvider.GetUtcNow());

        var run = new StoredRun(summary, [.. stances], moderator, plan);
        var state = Load();
        state.Runs.Add(run);
        while (state.Runs.Count > 32)
        {
            state.Runs.RemoveAt(0);
        }

        Save(state);

        // Project into corpus for later episode reads.
        await SendAsync(
            ICorpus.ForOwner(Id.Owner),
            new AppendCorpusEntry(
                CommandId.New(),
                Kind: "behavior.repo-review",
                Text: $"run={runId} files={stances.Count} rounds={moderator.Length} intent={synapse.Intent.Trim()}",
                Correlation: runId,
                At: summary.CompletedAt))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await FlushOutboxAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await ReplyAsync(new BehaviorRunStarted(synapse.CommandId, summary), cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task HandleAsync(ReadBehaviorRun synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        var run = Load().Runs.FirstOrDefault(r =>
                string.Equals(r.Summary.RunId, synapse.RunId, StringComparison.OrdinalIgnoreCase))
            ?? throw new NeuronAuthorizationException($"Unknown behavior run '{synapse.RunId}'.");

        return ReplyAsync(
            new BehaviorRunSnapshot(
                synapse.CommandId,
                run.Summary,
                run.Stances,
                run.Rounds,
                run.Plan),
            cancellationToken);
    }

    private static string[] ListFiles(string root, int limit)
    {
        return
        [
            .. Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(p =>
                {
                    var rel = Path.GetRelativePath(root, p);
                    return !rel.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                        && !rel.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                        && !rel.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
                })
                .Take(limit)
                .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static FileStance StanceFor(string root, string relative, string intent)
    {
        var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        var name = Path.GetFileName(relative);
        var dir = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? "";
        var lines = 0;
        try
        {
            lines = File.ReadLines(full).Take(500).Count();
        }
        catch
        {
            // unreadable → abstain
        }

        var intentHit = relative.Contains(intent.Split(' ').FirstOrDefault() ?? "___", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Neuron", StringComparison.OrdinalIgnoreCase);

        var stance = intentHit
            ? "change"
            : dir.Contains("Contracts", StringComparison.OrdinalIgnoreCase)
                ? "stabilize"
                : lines > 300
                    ? "review-carefully"
                    : "hold";

        var rationale = intentHit
            ? $"Path/name aligns with intent '{intent}'; ~{lines} lines."
            : $"Peripheral to intent; ~{lines} lines under {dir}.";

        var priority = stance switch
        {
            "change" => 1,
            "review-carefully" => 2,
            "stabilize" => 3,
            _ => 4,
        };

        return new FileStance(relative, stance, rationale, priority);
    }

    private static ModeratorRound[] Fold(IReadOnlyList<FileStance> stances, string intent, int rounds)
    {
        var ordered = stances.OrderBy(s => s.Priority).ThenBy(s => s.RelativePath).ToArray();
        var result = new ModeratorRound[rounds];
        for (var r = 1; r <= rounds; r++)
        {
            var take = Math.Max(3, ordered.Length / (rounds - r + 1));
            var focus = ordered.Take(take).Select(s => s.RelativePath).ToArray();
            var change = ordered.Count(s => s.Stance == "change");
            var hold = ordered.Count(s => s.Stance == "hold");
            result[r - 1] = new ModeratorRound(
                r,
                $"Round {r}/{rounds} on '{intent}': {change} change, {hold} hold, focusing {focus.Length} paths.",
                focus);
        }

        return result;
    }

    private static string WritePlan(
        string intent,
        string root,
        IReadOnlyList<FileStance> stances,
        IReadOnlyList<ModeratorRound> rounds)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Behavior plan: {intent}");
        sb.AppendLine();
        sb.AppendLine($"Repository: `{root}`");
        sb.AppendLine($"Files stanced: {stances.Count}");
        sb.AppendLine($"Moderator rounds: {rounds.Count}");
        sb.AppendLine();
        sb.AppendLine("## Recommended changes");
        foreach (var s in stances.Where(x => x.Stance == "change").Take(12))
        {
            sb.AppendLine($"- `{s.RelativePath}` — {s.Rationale}");
        }

        sb.AppendLine();
        sb.AppendLine("## Moderator fold");
        foreach (var round in rounds)
        {
            sb.AppendLine($"### Round {round.Round}");
            sb.AppendLine(round.Summary);
            foreach (var path in round.FocusPaths.Take(8))
            {
                sb.AppendLine($"  - {path}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Hold / stabilize");
        foreach (var s in stances.Where(x => x.Stance is "hold" or "stabilize").Take(8))
        {
            sb.AppendLine($"- `{s.RelativePath}` ({s.Stance})");
        }

        return sb.ToString();
    }

    private BehaviorState Load()
        => _state.Value is { Length: > 0 } serialized
            ? _states.Deserialize(serialized)
            : new BehaviorState();

    private void Save(BehaviorState state)
        => _state.Value = _states.SerializeToArray(state);

    private static void RequireCommand(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new NeuronAuthorizationException("A behavior command requires a command id.");
        }
    }
}
