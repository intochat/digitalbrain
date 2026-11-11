namespace DigitalBrain.Units;

/// <summary>The asks of one reaction, journaled in the same save as its next state.</summary>
/// <param name="Unit">The state alias of the unit that asked.</param>
/// <param name="Batch">The batch key under which the asks are stored.</param>
/// <param name="Asks">The asks returned by the reaction.</param>
[Signal("unit.asked")]
internal sealed record Asked(string Unit, string Batch, IReadOnlyList<Ask> Asks) : Signal;
