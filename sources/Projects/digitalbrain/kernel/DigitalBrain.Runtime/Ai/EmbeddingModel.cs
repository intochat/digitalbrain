namespace DigitalBrain.Runtime.Ai;

public abstract class EmbeddingModel
{
    public abstract string Id { get; }
    public abstract string Provider { get; }
    public abstract string DisplayName { get; }
    public abstract int Dimensions { get; }
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

    static readonly Lazy<IReadOnlyList<EmbeddingModel>> Discovered = new(()
        => [.. typeof(EmbeddingModel).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(EmbeddingModel)) && !t.IsAbstract)
            .Select(t => (EmbeddingModel)Activator.CreateInstance(t)!)]);

    public static IReadOnlyList<EmbeddingModel> All => Discovered.Value;
}
