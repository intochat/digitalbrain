using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.DevTools;
using DigitalBrain.Kernel;
using DigitalBrain.ProbeHost;
using Orleans;

var builder = WebApplication.CreateBuilder(args);

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
    var brain = Brain(grains);

    await brain.FireAsync(NeuronId.For<Recorder>(brain.Owner, "one"), new Remembered("a durable turn"));

    return Results.Ok();
});

app.MapGet("/probe/fired", async (IGrainFactory grains) =>
{
    var fired = await Brain(grains).Session.ReadJournalAsync(JournalKind.Outgoing, afterSequence: 0);
    var recorded = fired.ResetSnapshot?.TotalRecorded ?? fired.Delta.Count;

    return Results.Ok(recorded);
});

app.MapGet("/probe/delivered/{neuron}", async (string neuron, IGrainFactory grains) =>
{
    var delivered = await Brain(grains).Neuron(neuron, "one").ReadJournalAsync(
        JournalKind.Incoming,
        afterSequence: 0);
    var recorded = delivered.ResetSnapshot?.TotalRecorded ?? delivered.Delta.Count;

    return Results.Ok(recorded);
});

app.Run();

static BrainClient Brain(IGrainFactory grains) => new(grains, new OwnerId("hosted"));
