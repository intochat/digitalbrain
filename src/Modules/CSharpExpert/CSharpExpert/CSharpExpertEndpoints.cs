using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.CSharpExpert;

public static class CSharpExpertEndpoints
{
    public const string Prefix = "/csharp-expert";

    public static void MapCSharpExpert(this IEndpointRouteBuilder routes)
    {
        routes.MapGet($"{Prefix}/runs/{{runId}}", (string runId, IDigitalBrain brain, CancellationToken ct) =>
            Respond(async () => Results.Ok(await brain.Get<ICodingRun>(runId).Read().WaitAsync(ct))));

        routes.MapPost($"{Prefix}/runs/{{runId}}/clarify", (string runId, ClarifyCodingRun input, IDigitalBrain brain, CancellationToken ct) =>
            Respond(async () =>
            {
                var run = brain.Get<ICodingRun>(runId);
                await run.Clarify(input.Text).WaitAsync(ct);
                return Results.Ok(await run.Read().WaitAsync(ct));
            }));

        routes.MapPost($"{Prefix}/runs/{{runId}}/approve", (string runId, IDigitalBrain brain, CancellationToken ct) =>
            Respond(async () =>
            {
                var run = brain.Get<ICodingRun>(runId);
                await run.Approve().WaitAsync(ct);
                return Results.Ok(await run.Read().WaitAsync(ct));
            }));

        routes.MapPost($"{Prefix}/runs/{{runId}}/stop", (string runId, IDigitalBrain brain, CodingRunHost host, CancellationToken ct) =>
            Respond(async () =>
            {
                var run = brain.Get<ICodingRun>(runId);
                await run.Stop().WaitAsync(ct);
                await host.StopAsync(runId, ct);
                return Results.Ok(await run.Read().WaitAsync(ct));
            }));

        routes.MapPost($"{Prefix}/runs", (StartCodingRun input, CodingRunHost host, CancellationToken ct) =>
            Respond(async () => Results.Ok(await host.StartAsync(new FeatureRequest(input.SolutionPath, input.Description), ct))));
    }

    private static async Task<IResult> Respond(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (InvalidOperationException error)
        {
            return Results.Conflict(new { error = error.Message });
        }
        catch (ArgumentException error)
        {
            return Results.BadRequest(new { error = error.Message });
        }
    }
}

[GenerateSerializer, Alias("csharp-expert.start-run")]
public sealed record StartCodingRun(
    [property: Id(0)] string SolutionPath,
    [property: Id(1)] string Description);

[GenerateSerializer, Alias("csharp-expert.clarify-run")]
public sealed record ClarifyCodingRun(
    [property: Id(0)] string Text);
