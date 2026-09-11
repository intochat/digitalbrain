using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Excel;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal static class KitEndpoints
{
    public static IEndpointRouteBuilder MapKitEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapRead<IChart, ChartState>(endpoints, "/kit/charts/{name}", UIVocabulary.ChartType,
            static neuron => neuron.Read(), static state => state);
        MapRead<IGraph, GraphState>(endpoints, "/kit/graphs/{name}", UIVocabulary.GraphType,
            static neuron => neuron.Read(), static state => state);
        MapRead<IImage, ImageState>(endpoints, "/kit/images/{name}", UIVocabulary.ImageType,
            static neuron => neuron.Read(), static state => new KitImageStateResponse(state.Prompt, state.Model, state.MediaType));
        MapRead<ISpreadsheet, ExcelState>(endpoints, "/kit/spreadsheets/{name}", ExcelVocabulary.SpreadsheetType,
            static neuron => neuron.Read(), static state => state);
        MapRead<ISurface, SurfaceState>(endpoints, "/kit/surfaces/{name}", UIVocabulary.SurfaceType,
            static neuron => neuron.Read(), static state => state);

        endpoints.MapGet("/kit/images/{name}/content",
            static async Task<IResult> (string name, IGrainFactory grains, IKitImageStore imageStore, CancellationToken cancellationToken) =>
            {
                var neuron = grains.GetGrain<IImage>(new NeuronId(UIVocabulary.ImageType, name).ToGrainId());
                if (!await ExistsAsync(neuron).WaitAsync(cancellationToken))
                {
                    return Results.NotFound();
                }

                var state = await neuron.Read().WaitAsync(cancellationToken);
                var blob = await imageStore.ReadAsync(state.BlobName, cancellationToken);
                return blob is null ? Results.NotFound() : Results.File(blob.Value.Content, blob.Value.MediaType);
            }).AddEndpointFilter(new NeuronNameFilter("name"));

        return endpoints;
    }

    private static void MapRead<TNeuron, TState>(IEndpointRouteBuilder endpoints, string path, string neuronType,
        Func<TNeuron, Task<TState>> read, Func<TState, object> project) where TNeuron : INeuron
    {
        endpoints.MapGet(path,
            async Task<IResult> (string name, IGrainFactory grains, CancellationToken cancellationToken) =>
            {
                var neuron = grains.GetGrain<TNeuron>(new NeuronId(neuronType, name).ToGrainId());
                if (!await ExistsAsync(neuron).WaitAsync(cancellationToken))
                {
                    return Results.NotFound();
                }

                return Results.Ok(project(await read(neuron).WaitAsync(cancellationToken)));
            }).AddEndpointFilter(new NeuronNameFilter("name"));
    }

    // Two ReadOnly grain calls per kit read is the price of not putting kit-shape knowledge in the edge.
    private static async Task<bool> ExistsAsync(INeuron neuron) => (await neuron.ReadState()).Count > 0;
}

internal sealed record KitImageStateResponse(string Prompt, string Model, string MediaType);
