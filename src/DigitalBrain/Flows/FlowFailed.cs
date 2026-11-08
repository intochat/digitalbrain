namespace DigitalBrain.Flows;

/// <summary>Thrown by a step body to fail its flow with a detail.</summary>
/// <param name="step">The failing step.</param>
/// <param name="detail">Why it failed.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710", Justification = "FlowFailed is the spec name (6.1): a step body throws it to fail its flow.")]
public sealed class FlowFailed(string step, string detail) : Exception(detail)
{
    /// <summary>The failing step.</summary>
    public string Step => step;
}
