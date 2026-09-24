namespace DigitalBrain.Apps;

/// <summary>A host-provided, deterministic behavior. External effects belong in supervised workers.</summary>
public interface IAppBehavior
{
    string Id { get; }
    void Validate(AppPart part, AppComposition composition);
    string Execute(string value, AppPart part, IReadOnlyDictionary<string, string> configuration);
}
