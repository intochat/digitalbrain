namespace DigitalBrain.Spi;

/// <summary>Allows a neuron to provide an observable JSON state snapshot for live inspection and dashboards.</summary>
public interface IStateInspectable
{
    /// <summary>Inspects the current state of the neuron.</summary>
    Task<System.Text.Json.JsonElement?> InspectState(CancellationToken cancellationToken = default);
}
