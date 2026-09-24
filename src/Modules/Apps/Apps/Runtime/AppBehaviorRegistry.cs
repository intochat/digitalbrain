namespace DigitalBrain.Apps;

/// <summary>A host-provided, deterministic behavior. External effects belong in supervised workers.</summary>
public interface IAppBehavior
{
    string Id { get; }
    void Validate(AppPart part, AppComposition composition);
    string Execute(string value, AppPart part, IReadOnlyDictionary<string, string> configuration);
}

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

internal sealed class TextAppBehavior(string id) : IAppBehavior
{
    public string Id => id;
    public void Validate(AppPart part, AppComposition composition)
    {
        if (id is "text.prefix" or "text.suffix")
        {
            if (part.Settings.Count != 1 || !part.Settings.TryGetValue("configurationKey", out var key) || !composition.Defaults.ContainsKey(key))
            { throw new AppManifestException($"{id} requires configurationKey naming a declared configuration field."); }
        }
        else if (part.Settings.Count != 0) { throw new AppManifestException($"{id} accepts no settings."); }
    }
    public string Execute(string value, AppPart part, IReadOnlyDictionary<string, string> configuration) => id switch
    {
        "text.prefix" => configuration[part.Settings["configurationKey"]] + value,
        "text.suffix" => value + configuration[part.Settings["configurationKey"]],
        "text.uppercase" => value.ToUpperInvariant(),
        "text.lowercase" => value.ToLowerInvariant(),
        "text.trim" => value.Trim(),
        _ => value,
    };
}
