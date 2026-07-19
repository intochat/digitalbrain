using DigitalBrain;
using DigitalBrain.ProbeHost;
using Orleans;

var builder = WebApplication.CreateBuilder(args);

builder.UseOrleans(silo => silo.AddDigitalBrain().AddDigitalBrainJournalStorage(builder.Configuration));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("healthy"));

app.MapPost("/probe/turn", async (IGrainFactory grains) =>
{
    var brain = Brain(grains);

    await brain.FireAsync(NeuronId.For<Recorder>(brain.Owner, "one"), new Remembered("a durable turn"));

    return Results.Ok();
});

app.MapGet("/probe/fired", async (IGrainFactory grains) =>
{
    var fired = await Brain(grains).Session.ReadJournalAsync(JournalKind.Outgoing);

    return Results.Ok(fired.Count);
});

app.MapGet("/probe/remembered", async (IGrainFactory grains) =>
{
    var remembered = await Brain(grains).Neuron(nameof(Recorder), "one").ReadJournalAsync(JournalKind.Incoming);

    return Results.Ok(remembered.Count);
});

app.Run();

static BrainClient Brain(IGrainFactory grains) => new(grains, new OwnerId("hosted"));
