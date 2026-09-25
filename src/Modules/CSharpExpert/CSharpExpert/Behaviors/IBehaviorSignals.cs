namespace DigitalBrain.CSharpExpert;

// A behavior owns the list of signals it subscribes to, so a run session derives its ready condition
// from the behaviors themselves instead of a list kept in step with them by hand.
public interface IBehaviorSignals
{
    IReadOnlyList<Type> Signals { get; }
}
