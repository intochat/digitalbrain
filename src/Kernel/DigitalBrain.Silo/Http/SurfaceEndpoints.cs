using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal static class SurfaceEndpoints
{
    public static IEndpointRouteBuilder MapSurfaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/surfaces/{surfaceName}/open",
            static async Task<IResult> (string surfaceName, OpenSurfaceRequest request, IGrainFactory grains,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var surface = grains.GetGrain<ISurface>(new NeuronId(UIVocabulary.SurfaceType, surfaceName).ToGrainId());
                    var command = request.CommandId is { } id ? new CommandId(id) : CommandId.New();
                    var accepted = await surface.Open(new OpenSurface(command, request.SurfaceKey, request.Title, request.Root), cancellationToken);
                    return Results.Accepted(value: accepted);
                }
                catch (Exception error) when (EdgeResults.IsRejection(error))
                {
                    return EdgeResults.Rejected(error);
                }
            }).AddEndpointFilter(new NeuronNameFilter("surfaceName"));

        endpoints.MapGet("/surfaces/{surfaceName}",
            static async Task<IResult> (string surfaceName, IGrainFactory grains, CancellationToken cancellationToken) =>
            {
                var surface = grains.GetGrain<ISurface>(new NeuronId(UIVocabulary.SurfaceType, surfaceName).ToGrainId());
                return Results.Ok(await surface.Read().WaitAsync(cancellationToken));
            }).AddEndpointFilter(new NeuronNameFilter("surfaceName"));

        endpoints.MapPost("/surfaces/{surfaceName}/controls/{controlId}/activate",
            static async Task<IResult> (string surfaceName, string controlId, ActivateSurfaceControlRequest request,
                IGrainFactory grains, CancellationToken cancellationToken) =>
            {
                try
                {
                    var surface = grains.GetGrain<ISurface>(new NeuronId(UIVocabulary.SurfaceType, surfaceName).ToGrainId());
                    var state = await surface.Read().WaitAsync(cancellationToken);
                    var scene = state.Scenes.FirstOrDefault(candidate => candidate.SurfaceKey == request.SurfaceKey);
                    if (Find(scene?.Root, controlId) is null)
                    {
                        return Results.NotFound();
                    }

                    var accepted = await surface.Activate(
                        new ActivateControl(CommandId.New(), request.SurfaceKey, controlId, request.Intent), cancellationToken);
                    return Results.Accepted(value: accepted);
                }
                catch (Exception error) when (EdgeResults.IsRejection(error))
                {
                    return EdgeResults.Rejected(error);
                }
            }).AddEndpointFilter(new NeuronNameFilter("surfaceName"));

        endpoints.MapGet("/surfaces/{surfaceName}/events",
            static async Task (string surfaceName, long? afterSequence, IGrainFactory grains, StreamWake wake, SessionStreamOptions options,
                HttpContext http, CancellationToken cancellationToken) =>
            {
                var surfaceId = new NeuronId(UIVocabulary.SurfaceType, surfaceName);
                var surface = grains.GetGrain<ISurface>(surfaceId.ToGrainId());
                await SessionStream.RunAsync(http, surface, surfaceId, JournalKind.Outgoing,
                    afterSequence.GetValueOrDefault(), ProjectSurface, "surface",
                    async token => await surface.Read().WaitAsync(token), wake, options, cancellationToken);
            }).AddEndpointFilter(new NeuronNameFilter("surfaceName"));

        return endpoints;
    }

    private static SurfaceComponent? Find(SurfaceComponent? component, string controlId)
    {
        if (component is null)
        {
            return null;
        }

        if (component.Kind == "button" && component.Key == controlId)
        {
            return component;
        }

        foreach (var child in component.Children ?? [])
        {
            if (Find(child, controlId) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static SurfaceEvent? ProjectSurface(SignalDelivery delivery)
        => delivery.Signal.Type is UIVocabulary.SurfaceOpened or UIVocabulary.ComponentAdded
            or UIVocabulary.ControlActivated or UIVocabulary.ActivityChanged
            ? new SurfaceEvent(delivery.Sequence, delivery.Signal.Type, JsonNode.Parse(delivery.Signal.Body))
            : null;
}

internal sealed record OpenSurfaceRequest(Guid? CommandId, string SurfaceKey, string Title, SurfaceComponent? Root = null);

internal sealed record ActivateSurfaceControlRequest(string SurfaceKey, string Intent);

internal sealed record SurfaceEvent(long Sequence, string Signal, JsonNode? Body);
