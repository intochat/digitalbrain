using DigitalBrain;
using DigitalBrain.ProbeHost;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Orleans;

var builder = WebApplication.CreateBuilder(args);

var scripted = new ScriptedModel();
scripted.Answer("is the kernel awake?", "the kernel is awake");

builder.Services.AddKeyedSingleton<IChatClient>(ModelTier.Balanced, scripted);
builder.UseOrleans(silo => silo.AddDigitalBrain().AddDigitalBrainJournalStorage(builder.Configuration));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("healthy"));

app.MapPost("/probe/turn", async (IGrainFactory grains) =>
{
    var brain = Brain(grains);

    await brain.FireAsync(NeuronId.For<Recorder>(brain.Owner, "one"), new Remembered("a durable turn"));

    return Results.Ok();
});

app.MapPost("/probe/ask", async (IGrainFactory grains) =>
{
    var brain = Brain(grains);

    await brain.FireAsync(NeuronId.For<Asker>(brain.Owner, "one"), new Asked("is the kernel awake?"));

    return Results.Ok();
});

app.MapGet("/probe/fired", async (IGrainFactory grains) =>
{
    var fired = await Brain(grains).Session.ReadJournalAsync(JournalKind.Outgoing);

    return Results.Ok(fired.Count);
});

app.MapGet("/probe/delivered/{neuron}", async (string neuron, IGrainFactory grains) =>
{
    var delivered = await Brain(grains).Neuron(neuron, "one").ReadJournalAsync(JournalKind.Incoming);

    return Results.Ok(delivered.Count);
});

app.MapGet("/probe/answers", async (IGrainFactory grains) =>
{
    var answered = await Brain(grains).Neuron(nameof(Asker), "one").ReadJournalAsync(JournalKind.Outgoing);

    return Results.Ok(string.Join("|", answered.OfType<IAnswer>().Select(answer => answer.Text)));
});

app.Run();

static BrainClient Brain(IGrainFactory grains) => new(grains, new OwnerId("hosted"));
