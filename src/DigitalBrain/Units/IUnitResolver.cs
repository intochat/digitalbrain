namespace DigitalBrain.Units;

/// <summary>Resolves a state alias for the specified brain and view to a unit handle.</summary>
public interface IUnitResolver
{
    /// <summary>Resolves a state alias for the specified brain and view to a refcounted unit handle.</summary>
    Task<UnitHandle?> Resolve(BrainId brain, string alias, UnitView view, CancellationToken ct = default);
}
