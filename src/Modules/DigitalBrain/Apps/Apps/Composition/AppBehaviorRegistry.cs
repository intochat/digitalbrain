namespace DigitalBrain.Apps;

public sealed class AppBehaviorRegistry(IEnumerable<IAppBehavior> behaviors)
{
    private readonly IReadOnlyDictionary<string, IAppBehavior> _behaviors = behaviors.ToDictionary(x => x.Id, StringComparer.Ordinal);
    public IAppBehavior Get(string id) => _behaviors.TryGetValue(id, out var behavior) ? behavior : throw new AppManifestException($"Behavior '{id}' is not available on this host.");
    public void Validate(AppComposition composition)
    {
        AppCompositionValidation.Validate(composition);
        foreach (var part in composition.Parts) { Get(part.Behavior).Validate(part, composition); }
        foreach (var binding in composition.Bindings.Where(b => b.Source is not ("$app" or "$brain")))
        { if (binding.Signal != "completed") { throw new AppManifestException("These behaviors emit the completed signal."); } }
    }
}
