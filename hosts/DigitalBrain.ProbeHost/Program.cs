using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.DevTools;
using DigitalBrain.Kernel;
using DigitalBrain.ProbeHost;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Orleans;

var builder = WebApplication.CreateBuilder(args);

var scripted = new ScriptedModel();
scripted.Answer("is the kernel awake?", "the kernel is awake");

builder.Services.AddKeyedSingleton<IChatClient>(ModelTier.Balanced, scripted);
builder.UseOrleans(silo => silo
    .AddDigitalBrain()
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

app.MapPost("/probe/ask", async (IGrainFactory grains) =>
{
    var brain = Brain(grains);

    await brain.FireAsync(NeuronId.For<Asker>(brain.Owner, "one"), new Asked("is the kernel awake?"));

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

app.MapGet("/probe/answers", async (IGrainFactory grains) =>
{
    var answered = await Brain(grains).Neuron(nameof(Asker), "one").ReadJournalAsync(
        JournalKind.Outgoing,
        afterSequence: 0);

    return answered.ResetSnapshot is not null
        ? Results.Problem(
            "The answer journal compacted before its payloads were read.",
            statusCode: StatusCodes.Status409Conflict)
        : Results.Ok(string.Join("|", answered.Delta.OfType<IAnswer>().Select(answer => answer.Text)));
});

app.Run();

static BrainClient Brain(IGrainFactory grains) => new(grains, new OwnerId("hosted"));
