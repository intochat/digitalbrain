namespace DigitalBrain.Apps;

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
