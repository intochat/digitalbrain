using DigitalBrain.Contracts;

namespace DigitalBrain;

/// <summary>Represents an installed runtime unit actor that can handle asks.</summary>
[NeuronAlias("unit")]
public interface IUnit
{
    /// <summary>Handles an ask dispatched to this unit.</summary>
    /// <param name="ask">The ask to handle.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The settled answer.</returns>
    Task<Answer> Handle(Ask ask, CancellationToken ct = default);
}
