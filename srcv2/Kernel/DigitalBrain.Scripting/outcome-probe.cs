#:project ../Aspire/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj
#:project ../../Modules/Time/Contracts/DigitalBrain.Modules.Time.Contracts.csproj
#:project ../../Modules/UI/DigitalBrain.Modules.UI.Contracts/DigitalBrain.Modules.UI.Contracts.csproj
#:project ../../Modules/AI/Contracts/DigitalBrain.Modules.AI.Contracts.csproj
#:project ../../Modules/Execution/Contracts/DigitalBrain.Modules.Execution.Contracts.csproj
#:property TreatWarningsAsErrors=false
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true

using DigitalBrain.Abstractions;
using DigitalBrain.Aspire;
using DigitalBrain.Client;
using DigitalBrain.Time;

// Stage 1 acceptance probe. Both assertions FAIL against HEAD, and that failure is the
// defect being fixed: a settled refusal reaches nobody, and a zero-receiver emission is
// journaled and silently dropped.

var brain = await DigitalBrainClient.ConnectAsync(args);
using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(40));
var failures = new List<string>();

// A — a settled refusal must come back carrying its reason, not silence.
// The morph names a field that does not exist on the target contract, so
// SynapseGraphNeuron.RequireWorkingTransform refuses and names the real fields.
try
{
    await brain.Get<ISynapseGraph>(ISynapseGraph.InstanceName).FireAsync<Connected>(
        new Connect(
            Guid.NewGuid(),
            new NeuronId("chart", brain.Owner, "outcome-probe"),
            "ui.chart-point",
            new NeuronId("chat", brain.Owner, "outcome-probe"),
            "to:ui.note{NoSuchFieldOnNote=Value}"),
        patience.Token);

    failures.Add("A: the malformed morph was ACCEPTED — connect-time validation did not refuse.");
}
catch (NeuronAuthorizationException refused) when (!string.IsNullOrWhiteSpace(refused.Message))
{
    Console.WriteLine($"A PASS — refusal reached the caller: {refused.Message}");
}
catch (OperationCanceledException)
{
    failures.Add(
        "A: the refusal never arrived; the caller waited to its own deadline. "
        + "This is the HEAD defect — the reason died in NeuronOutbox as a telemetry span.");
}

// B — an emission that resolves zero receivers must leave a durable Unrouted record.
// TimerElapsed carries an alias and no class in src declares IHandle<TimerElapsed>, so it
// mints no broadcast ghost; with no graph connection from the session it reaches nobody.
var sessionId = ISessionNeuron.ForOwner(brain.Owner);
var opened = await brain.ReadJournalAsync(sessionId, JournalKind.Incoming, long.MaxValue, patience.Token);
var cursor = opened.ResumeSequence;

await brain.FireAsync(
    new TimerElapsed(
        new NeuronId("timer", brain.Owner, "outcome-probe"),
        Generation: 1,
        ScheduledAt: DateTimeOffset.UtcNow,
        DueAt: DateTimeOffset.UtcNow,
        ObservedAt: DateTimeOffset.UtcNow,
        Resolution: TimerResolution.OnTime,
        Note: "outcome probe"),
    patience.Token);

var unrouted = false;
try
{
    while (!unrouted)
    {
        var page = await brain.ReadJournalAsync(sessionId, JournalKind.Incoming, cursor, patience.Token);

        if (page.ResetSnapshot is not null)
        {
            failures.Add("B: the session journal compacted past the cursor; the record is unknowable.");
            break;
        }

        foreach (var delivery in page.Delta)
        {
            if (delivery.Synapse is Unrouted record
                && string.Equals(record.Alias, "time.timer-elapsed", StringComparison.Ordinal))
            {
                Console.WriteLine($"B PASS — zero-receiver emission recorded: {record.Alias} from {record.Source}");
                unrouted = true;
                break;
            }
        }

        cursor = page.ResumeSequence;

        if (!unrouted)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), patience.Token);
        }
    }
}
catch (OperationCanceledException)
{
    failures.Add(
        "B: no db.unrouted was recorded; the emission vanished. "
        + "This is the HEAD defect — a zero-receiver emission creates no outbox entry and no record.");
}

foreach (var failure in failures)
{
    Console.WriteLine($"FAIL — {failure}");
}

Console.WriteLine(failures.Count == 0
    ? "outcome-probe: PASS (2/2)"
    : $"outcome-probe: FAIL ({2 - failures.Count}/2)");

Environment.ExitCode = failures.Count == 0 ? 0 : 1;
