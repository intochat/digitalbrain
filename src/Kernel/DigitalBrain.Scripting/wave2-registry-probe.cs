#:project ../../Aspire/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj
#:property TreatWarningsAsErrors=false
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true

using DigitalBrain.Abstractions;
using DigitalBrain.Aspire;
using DigitalBrain.Client;

var brain = await DigitalBrainClient.ConnectAsync(args);
using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(90));
var registryId = IRegistry.ForOwner(brain.Owner);

await brain.FireAsync(
    registryId,
    new RegisterInstance(
        CommandId.New(),
        new NeuronId("chart", brain.Owner, "sales-cold"),
        Role: "chart",
        Enabled: true,
        Note: "cold chart for walkthrough"),
    patience.Token);
Console.WriteLine("FIRED register sales-cold");
await Task.Delay(500, patience.Token);

await brain.FireAsync(
    registryId,
    new RegisterInstance(
        CommandId.New(),
        new NeuronId("timer", brain.Owner, "nightly"),
        Role: "schedule",
        Note: "idle schedule"),
    patience.Token);
Console.WriteLine("FIRED register nightly");
await Task.Delay(500, patience.Token);

await brain.FireAsync(
    registryId,
    new InstallBundle(
        CommandId.New(),
        Name: "wave2-board",
        Members:
        [
            new BundleMember("chart", "board-kpi", "chart", "bundle chart"),
            new BundleMember("timer", "bundle-tick", "schedule", "bundle schedule"),
        ],
        Wires: [],
        Intent: "wave2 gate"),
    patience.Token);
Console.WriteLine("FIRED install-bundle wave2-board");
await Task.Delay(2000, patience.Token);

// Connect validation via fire-and-forget (no reply poll — scripting client lacks full synapse codecs).
await brain.FireAsync(
    ISynapseGraph.ForOwner(brain.Owner),
    new Connect(
        Guid.NewGuid(),
        new NeuronId("timer", brain.Owner, "nightly"),
        "time.timer-elapsed",
        new NeuronId("chat", brain.Owner, "main"),
        Transform: null,
        Intent: "should refuse — chat does not handle raw timer-elapsed"),
    patience.Token);
Console.WriteLine("FIRED connect raw (expect refuse)");
await Task.Delay(800, patience.Token);

await brain.FireAsync(
    ISynapseGraph.ForOwner(brain.Owner),
    new Connect(
        Guid.NewGuid(),
        new NeuronId("timer", brain.Owner, "nightly"),
        "time.timer-elapsed",
        new NeuronId("chat", brain.Owner, "main"),
        Transform: "to:ui.note{Text=Note}",
        Intent: "wave2 valid morph"),
    patience.Token);
Console.WriteLine("FIRED connect morph");
await Task.Delay(1000, patience.Token);

Console.WriteLine("WAVE2_PROBE_OK");
