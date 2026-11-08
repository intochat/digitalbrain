using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Runtime.Synapses;
using DigitalBrain.Spi;

namespace DigitalBrain.Testing.Synapses;

/// <summary>
/// Runs a synapse source against scenario steps before it is deployed for real: compiles it with <see cref="SynapseCompiler"/>, deploys
/// it on a fresh <see cref="TestOS.Local"/> with its slots bound to <see cref="FakeSlots"/>, drives the steps and returns a transcript.
/// A source that fails to compile, to pass the host boundary or to deploy is a failed transcript, not an exception.
/// </summary>
public static class UnitScenario
{
    private static readonly BrainId Brain = NeuronId.Parse("scenario/x/y").Brain;
    private static readonly Lazy<SynapseCompiler> Compiler = new(() => new SynapseCompiler(ReferenceSet.FromDefaultContext()));

    /// <summary>
    /// Runs <paramref name="steps"/> against <paramref name="source"/>. Every install the source asks for is approved as the person would;
    /// the needs sentences approved are listed in <see cref="ScenarioTranscript.Consented"/> so the caller can show them.
    /// </summary>
    /// <param name="source">The synapse source.</param>
    /// <param name="fakes">The fakes its slots bind to, by slot name.</param>
    /// <param name="steps">The steps, in order.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The transcript.</returns>
    public static async Task<ScenarioTranscript> Run(SynapseSource source, FakeSlots fakes, IReadOnlyList<ScenarioStep> steps, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fakes);
        ArgumentNullException.ThrowIfNull(steps);

        var build = Compiler.Value.Compile(source);
        if (!build.Success)
        {
            return Failed([.. build.Diagnostics.Select(d => $"compile {d.File}({d.Line}) {d.Id}: {d.Message}")]);
        }

        IReadOnlyList<string> aliases;
        try { aliases = await StateAliases(build).ConfigureAwait(false); }
        catch (Exception e) when (e is InvalidOperationException or BadImageFormatException or TypeLoadException or System.Reflection.ReflectionTypeLoadException)
        {
            return Failed([$"load {e.GetType().Name}: {e.Message}"]);
        }

        await using var os = TestOS.Local();
        fakes.BindTo(os);
        os.Register<ScenarioInput>();
        os.Confirmations.ApproveAll();

        var outcome = await os.Synapses.Deploy(Brain, source, Bindings(source, aliases, fakes), ct).ConfigureAwait(false);
        await os.Idle().ConfigureAwait(false);
        if (outcome.Status != DeployStatus.Deployed)
        {
            return Failed([$"deploy {outcome.Status}: {outcome.Reason}", .. outcome.Diagnostics.Select(d => $"deploy {d.File}({d.Line}) {d.Id}: {d.Message}")]);
        }

        var refused = outcome.Installs.Where(i => i.Value.Status == InstallStatus.Refused).Select(i => $"install {i.Key}: {i.Value.Reason}").ToList();
        if (refused.Count > 0) { return Failed(refused); }

        var consented = outcome.Installs.Values.Where(i => i.Status != InstallStatus.Refused && !string.IsNullOrEmpty(i.Sentence))
            .Select(i => i.Sentence).ToList();
        var journal = os.Journal(Brain);
        var position = (await journal.Read(long.MaxValue, 1, ct).ConfigureAwait(false)).Last;
        var seenFailures = os.Failures.Count;
        var events = new List<ScenarioEvent>();
        var failures = new List<string>();
        var input = os.Get<IScenarioInput>(NeuronId.Parse($"{Brain}/{ScenarioInput.Alias}/default"));
        var window = 0; // the first event of the last stimulus step

        for (var i = 0; i < steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = steps[i];
            if (step is Publish or Tick) { window = events.Count; }

            try
            {
                switch (step)
                {
                    case Publish publish:
                        await input.Publish(publish.SignalAlias, publish.Payload).ConfigureAwait(false);
                        await os.Idle().ConfigureAwait(false);
                        break;
                    case Tick tick:
                        var now = os.Clock.GetUtcNow();
                        if (tick.At <= now)
                        {
                            failures.Add($"step {i} Tick: At is not after now ({tick.At:O} <= {now:O})");
                            break;
                        }

                        os.Clock.Advance(tick.At - now);
                        await os.Idle().ConfigureAwait(false);
                        break;
                    case ExpectState expect:
                        if (await CheckState(os, expect, ct).ConfigureAwait(false) is { } problem)
                        {
                            failures.Add($"step {i} ExpectState {expect.Alias} {expect.JsonPath}: {problem}");
                        }

                        break;
                    case ExpectSignal expect:
                        if (!events.Skip(window).Any(e => string.Equals(SignalAliases.Of(e.Signal.Signal), expect.Alias, StringComparison.Ordinal)))
                        {
                            failures.Add($"step {i} ExpectSignal {expect.Alias}: no such signal since the previous step");
                        }

                        break;
                    default:
                        failures.Add($"step {i} {step.GetType().Name}: unknown step");
                        break;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failures.Add($"step {i} {step.GetType().Name}: {e.GetType().Name}: {e.Message}");
            }

            foreach (var error in os.Failures.Skip(seenFailures)) { failures.Add($"step {i} {step.GetType().Name}: {error.GetType().Name}: {error.Message}"); }
            seenFailures = os.Failures.Count;
            position = await Drain(journal, position, i, events, ct).ConfigureAwait(false);
        }

        var views = new List<PublishedView>();
        foreach (var alias in aliases)
        {
            if (await os.Get<IViewReader>(NeuronId.Parse($"{Brain}/{alias}/default")).Read(alias).ConfigureAwait(false) is { } view) { views.Add(view); }
        }

        return new ScenarioTranscript(failures.Count == 0, events, failures, consented, views);
    }

    private static ScenarioTranscript Failed(List<string> failures) => new(false, [], failures.Count == 0 ? ["failed"] : failures, [], []);

    // The aliases the compiled source declares, scanned from the build the host would load; the load context is unloaded again.
    private static async Task<IReadOnlyList<string>> StateAliases(SynapseBuild build)
    {
        var loader = new SynapseLoader();
        var loaded = loader.Load(build);
        var aliases = loaded.Catalog.States.Keys.ToList();
        _ = await loader.Unload(loaded, gcRounds: 0).ConfigureAwait(false);
        return aliases;
    }

    private static async Task<long> Drain(TestJournal journal, long position, int step, List<ScenarioEvent> events, CancellationToken ct)
    {
        while (true)
        {
            var page = await journal.Read(position, SignalPage.MaxLimit, ct).ConfigureAwait(false);
            if (page.Signals.Count == 0) { return Math.Max(position, page.Last); }
            foreach (var signal in page.Signals) { events.Add(new ScenarioEvent(step, signal)); }
            position = page.Signals[^1].Sequence;
        }
    }

    private static async Task<string?> CheckState(TestOS os, ExpectState expect, CancellationToken ct)
    {
        var state = await ((IBrainObserver)os).State(NeuronId.Parse($"{Brain}/{expect.Alias}/default"), ct).ConfigureAwait(false);
        if (state is not { } root) { return "no state"; }
        var node = root;
        foreach (var part in expect.JsonPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (node.ValueKind != JsonValueKind.Object || !TryProperty(node, part, out node)) { return $"no '{part}' in {Short(root)}"; }
        }

        return JsonElement.DeepEquals(node, expect.Expected) ? null : $"expected {expect.Expected.GetRawText()}, was {node.GetRawText()}";
    }

    private static bool TryProperty(JsonElement node, string name, out JsonElement value)
    {
        foreach (var property in node.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string Short(JsonElement json)
    {
        var text = json.GetRawText();
        return text.Length <= 200 ? text : string.Concat(text.AsSpan(0, 200), "...");
    }

    // Every alias the source declares binds every fake by its slot name; a binding the alias does not use is ignored. The source's own
    // bindings stay for the slots no fake names.
    private static Dictionary<string, IReadOnlyDictionary<string, string>> Bindings(SynapseSource source, IReadOnlyList<string> aliases, FakeSlots fakes)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var alias in aliases)
        {
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
            if (source.Bindings is { } all && all.TryGetValue(alias, out var own))
            {
                foreach (var (slot, connection) in own) { bindings[slot] = connection; }
            }

            foreach (var slot in fakes.Slots) { bindings[slot] = slot; }
            result[alias] = bindings;
        }

        return result;
    }

}

/// <summary>The scenario's own publisher: journals a signal by alias into the brain.</summary>
[NeuronAlias(ScenarioInput.Alias)]
internal interface IScenarioInput
{
    Task Publish(string alias, JsonElement payload);
}

/// <summary>The count of signals the scenario published.</summary>
internal sealed record ScenarioInputState(int Published = 0)
{
    public ScenarioInputState() : this(0) { }
}

[Neuron(Alias)]
internal sealed class ScenarioInput : Neuron<ScenarioInputState>, IScenarioInput
{
    internal const string Alias = "scenario.input";

    public Task Publish(string alias, JsonElement payload) =>
        Save(State with { Published = State.Published + 1 }, new EmittedSignal(alias, payload.Clone()));
}
