using DigitalBrain.Abstractions;
using DigitalBrain.DevTools;
using DigitalBrain.Kernel;
using DigitalBrain.ProbeHost;
using Orleans;

var builder = WebApplication.CreateBuilder(args);

builder.AddKeyedAzureTableServiceClient("probe-clustering");
builder.AddKeyedAzureTableServiceClient("probe-reminders");
builder.UseOrleans(silo => silo
    .AddDigitalBrain()
    .AddBroadcastHandlers(typeof(Recorder).Assembly)
    .AddDigitalBrainJournalStorage(builder.Configuration)
    .AddDigitalBrainDevTools(builder.Environment));

var app = builder.Build();

app.MapDigitalBrainDevTools(app.Environment);
app.MapGet("/health", () => Results.Ok("healthy"));

app.MapPost("/probe/turn", async (IGrainFactory grains) =>
{
    var owner = Owner();

    await Session(grains, owner).Fire(
        NeuronId.For<Recorder>(owner, "one"),
        new Remembered("a durable turn"));

    return Results.Ok();
});

app.MapGet("/probe/fired", async (IGrainFactory grains) =>
{
    var owner = Owner();
    var fired = await Session(grains, owner).ReadNeuronJournal(
        SessionId(owner),
        JournalKind.Outgoing,
        afterSequence: 0);
    var recorded = fired.ResetSnapshot?.TotalRecorded ?? fired.Delta.Count;

    return Results.Ok(recorded);
});

app.MapGet("/probe/delivered/{neuron}", async (string neuron, IGrainFactory grains) =>
{
    var owner = Owner();
    var delivered = await Session(grains, owner).ReadNeuronJournal(
        new NeuronId(neuron, owner, "one"),
        JournalKind.Incoming,
        afterSequence: 0);
    var recorded = delivered.ResetSnapshot?.TotalRecorded ?? delivered.Delta.Count;

    return Results.Ok(recorded);
});

app.Run();

static OwnerId Owner() => new("hosted");

static NeuronId SessionId(OwnerId owner) => new(ISessionNeuron.GrainTypeName, owner, "session");

static ISessionNeuron Session(IGrainFactory grains, OwnerId owner)
    => grains.GetGrain<ISessionNeuron>(SessionId(owner).ToGrainId());
