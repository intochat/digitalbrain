namespace DigitalBrain.Testing.Mechanics;

public sealed record MechanicsStart(bool Echo = false, bool Audit = false) : Synapse;
