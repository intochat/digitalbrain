namespace DigitalBrain.SDK.DigitalBrain.Ai.Models;

public abstract class LlmModel
{
    public abstract string Id { get; }
    public abstract string Provider { get; }
    public abstract string DisplayName { get; }
    public virtual string Icon => Provider;

    public string ServiceKey
    {
        get
        {
            var normalized = Id.ToLowerInvariant()
                .Replace(".", "").Replace(":", "-").Replace("/", "-");
            return $"{Provider.ToLowerInvariant()}-{normalized}";
        }
    }

    static readonly Lazy<IReadOnlyList<LlmModel>> Discovered = new(()
        => [.. typeof(LlmModel).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(LlmModel)) && !t.IsAbstract)
            .Select(t => (LlmModel)Activator.CreateInstance(t)!)]);

    public static IReadOnlyList<LlmModel> All => Discovered.Value;
}
