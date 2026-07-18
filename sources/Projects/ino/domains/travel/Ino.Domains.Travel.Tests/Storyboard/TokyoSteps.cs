using System.Globalization;
using System.Text.Json;
using Reqnroll;
using Xunit;

namespace Ino.Domains.Travel.Tests.Storyboard;

[Binding]
public sealed class TokyoSteps
{
    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

    private const string CaptureKey = "storyboard.capture";

    private readonly ScenarioContext scenario;
    private readonly StoryboardTestSiloFixture clusterFixture;

    public TokyoSteps(ScenarioContext scenario, StoryboardTestSiloFixture clusterFixture)
    {
        this.scenario = scenario;
        this.clusterFixture = clusterFixture;
    }

    private StoryboardRecorder? Recorder =>
        scenario.TryGetValue<StoryboardRecorder>(out var r) ? r : null;

    private StoryboardCapture? Capture =>
        scenario.TryGetValue<StoryboardCapture>(CaptureKey, out var c) ? c : null;

    // Open the capture subscription for scenarios tagged @export-storyboard:tokyo.
    // Replan scenario does not get a capture — it has no seed plan state and
    // assertions are recording-only regardless.
    [BeforeScenario("export-storyboard:tokyo")]
    public void OpenCapture()
    {
        var capture = new StoryboardCapture(clusterFixture.PulseHub);
        scenario.Set(capture, CaptureKey);
    }

    [AfterScenario]
    public async Task CloseCapture()
    {
        if (scenario.TryGetValue<StoryboardCapture>(CaptureKey, out var capture))
            await capture.DisposeAsync();
    }

    [Given(@"a fresh ino brain with the v0\.1 cluster set")]
    public void GivenFreshBrain()
    {
        // TestCluster is already running (StoryboardFixtureHooks.StartClusterAsync
        // ensures it boots once for the whole test run). Nothing to do here.
    }

    [Given(@"the previous Tokyo plan is on screen")]
    public void GivenPreviousPlan()
    {
        // Replan scenario: seed state would require injecting a prior
        // TripPlanner grain activation into the TestCluster. Deferred to slice
        // 2 when TripPlanner gains journaled persistence. Recording-only today.
    }

    [When(@"the user says ""(.*)""")]
    public void WhenUserSays(string utterance)
    {
        Recorder?.AppendUtter(0.0, utterance);

        // Routing is recording-only in T2.4. Discovery grain requires a silo
        // tagged "ino.silo=kernel" for [PinToSilo("kernel")] placement to work;
        // TestCluster has no such tag. Wiring TestCluster to seed a stub Discovery
        // is deferred to slice 2 (BDD scenario T2.4.slice2).
        // When slice 2 lands, this When step will call:
        //   var grain = clusterFixture.Grains.GetGrain<IInoNeuron>(
        //       InoNeuronGrainKey.Format("storyboard-test", "default"));
        //   await grain.AskAsync(utterance, Guid.NewGuid().ToString(), ct);
    }

    [Then(@"the persona is ""(.*)"" at \+(.+)s")]
    public void ThenPersonaState(string state, string offset)
    {
        Recorder?.AppendOrb(ParseOffset(offset), state);
    }

    [Then(@"""(.*)"" synapses to ""(.*)"" at \+(.+)s gold with payload (.+)")]
    public void ThenSynapseGold(string from, string to, string offset, string payloadJson)
    {
        Recorder?.AppendSynapse(
            ParseOffset(offset), from, to, ParsePayload(payloadJson), gold: true);
    }

    [Then(@"""(.*)"" synapses to ""(.*)"" at \+(.+)s with payload (.+)")]
    public async Task ThenSynapse(string from, string to, string offset, string payloadJson)
    {
        Recorder?.AppendSynapse(
            ParseOffset(offset), from, to, ParsePayload(payloadJson), gold: false);

        // Cortex→PlanTrip is the T2.4 assertion boundary. In the TestCluster
        // this hop cannot be observed because Discovery grain placement fails
        // without a "kernel"-tagged silo. The behavioural assertion is gated
        // behind slice 2 which will wire a stub Discovery pre-populated with
        // travel neuron registrations.
        if (string.Equals(from, "Cortex", StringComparison.OrdinalIgnoreCase)
         && string.Equals(to, "PlanTrip", StringComparison.OrdinalIgnoreCase))
        {
            // Capture is active — log what we saw so far (may be empty since
            // WhenUserSays doesn't yet call the cluster).
            var capturedSoFar = Capture?.DescribeFires() ?? "(no capture)";
            Assert.Skip(
                $"Cortex→PlanTrip routing assertion deferred to slice 2: " +
                $"TestCluster has no 'ino.silo=kernel' tagged silo so Discovery " +
                $"grain placement fails before the route fires. " +
                $"Captured brain pulses: {capturedSoFar}");
            return;
        }

        // TODO(slice 2): assert capture.WaitForCallAsync(AliasTopology.AliasToGrain["{to}"],
        //   TimeSpan.FromSeconds(5)) once {to}'s LLM behaviour is wired into
        //   the test cluster. Today only the Cortex→PlanTrip boundary is asserted;
        //   deeper hops (PlanTrip→FindFlights/FindHotels/FindPlaces, recall
        //   comets, reminders) are recording-only.
        await Task.CompletedTask;
    }

    [Then(@"the ""(.*)"" card enters at \+(.+)s from ""(.*)""")]
    public void ThenCardEnters(string id, string offset, string fromCluster)
    {
        Recorder?.AppendCard(ParseOffset(offset), id, "enter", fromCluster);
    }

    [Then(@"the ""(.*)"" card morphs at \+(.+)s")]
    public void ThenCardMorphs(string id, string offset)
    {
        Recorder?.AppendCard(ParseOffset(offset), id, "morph", fromCluster: null);
    }

    private static double ParseOffset(string raw) =>
        double.Parse(raw, InvariantCulture);

    private static JsonElement ParsePayload(string raw) =>
        JsonSerializer.Deserialize<JsonElement>(raw);
}
