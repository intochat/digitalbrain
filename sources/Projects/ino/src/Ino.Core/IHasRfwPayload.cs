namespace Ino.Core;

/// <summary>
/// Marker interface implemented by <see cref="ISynapse"/> response types that
/// carry a pre-rendered Remote Flutter Widget payload. The gateway reads these
/// fields when shaping transport-level responses (gRPC <c>ChatResponse</c>,
/// MCP tool result, CLI stdout) so the rendering layer stays co-located with
/// the neuron that produced the data. Slice 1.2 introduced this so the
/// gateway can ship RFW bytes without referencing any neuron's UI
/// templates. Later slices may generalise via a dispatch table of
/// <c>IRfwRenderer&lt;T&gt;</c> registrations keyed by response synapse type.
/// </summary>
public interface IHasRfwPayload
{
    /// <summary>RFW description bytes (template DSL).</summary>
    byte[] RfwDescription { get; }

    /// <summary>RFW data bytes (JSON bound into the template).</summary>
    byte[] RfwData { get; }

    /// <summary>Logical content type — maps to gRPC <c>content_type</c> and
    /// drives Flutter-side dispatch (e.g. <c>flight_results</c>,
    /// <c>hotel_results</c>).</summary>
    string ContentType { get; }
}
