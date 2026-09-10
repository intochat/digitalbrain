using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GrainType(UIVocabulary.ChartType)]
internal sealed class ChartNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ChartState> state)
    : Neuron<ChartState>(runtime, state), IChart
{
    public Task<Accepted<string>> Render(RenderChart command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("render"), command, UIJson.Default.RenderChart, UIJson.Default.AcceptedString, arguments =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.Title);
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.ChartKind);
            ArgumentNullException.ThrowIfNull(arguments.Points);
            var work = Schedule(UIBodies.Signal(UIVocabulary.ChartRendering, arguments, UIJson.Default.RenderChart));
            return new Accepted<string>(Id.Name, work);
        });

    public Task<Accepted<string>> Append(AppendChartPoint command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("append"), command, UIJson.Default.AppendChartPoint, UIJson.Default.AcceptedString, arguments =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.Title);
            ArgumentNullException.ThrowIfNull(arguments.Point);
            var work = Schedule(UIBodies.Signal(UIVocabulary.ChartAppending, arguments, UIJson.Default.AppendChartPoint));
            return new Accepted<string>(Id.Name, work);
        });

    [ReadOnly]
    public Task<ChartState> Read() => Task.FromResult(State ?? new ChartState("", "line", []));

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        ChartState next;
        switch (delivery.Signal.Type)
        {
            case UIVocabulary.ChartRendering:
                var render = UIBodies.Read(delivery, UIJson.Default.RenderChart);
                next = new ChartState(render.Title, render.ChartKind, render.Points);
                break;
            case UIVocabulary.ChartAppending:
                var append = UIBodies.Read(delivery, UIJson.Default.AppendChartPoint);
                var current = State ?? new ChartState(append.Title.Trim(), "line", []);
                next = !string.IsNullOrWhiteSpace(append.Point.EventId)
                    && current.Points.Any(point => point.EventId == append.Point.EventId)
                    ? current
                    : current with { Points = [.. current.Points, append.Point] };
                break;
            default:
                return;
        }

        await SaveAsync(next, cancellationToken).ConfigureAwait(true);
        await FireAsync(UIBodies.Card(UIVocabulary.ChartRendered, Id.Name, next.Title),
            cancellationToken: cancellationToken).ConfigureAwait(true);
    }
}
