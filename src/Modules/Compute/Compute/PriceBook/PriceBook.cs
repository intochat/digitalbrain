using System.Text.Json;

namespace DigitalBrain.Compute;

// The one versioned price book: per concrete model, four token classes priced per million tokens.
// Local models are priced at zero and labelled local so a shadow receipt can say "not charged".
public sealed class PriceBook : IPriceBook
{
    internal const string ResourceName = "DigitalBrain.Compute.PriceBook.pricebook.v0.json";
    private const decimal PerMillionTokens = 1_000_000m;
    private static readonly string[] TokenClasses = ["input", "cached", "reasoning", "output"];
    private readonly IReadOnlyDictionary<string, decimal> _computePerMillion;
    private readonly IReadOnlySet<string> _localModels;

    public PriceBook() : this(OpenEmbedded()) { }

    public PriceBook(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Version = root.TryGetProperty("version", out var version) ? version.GetString() ?? "v0" : "v0";
        var compute = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var local = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in root.GetProperty("models").EnumerateArray())
        {
            var id = model.GetProperty("id").GetString()!;
            if (model.TryGetProperty("local", out var localFlag) && localFlag.GetBoolean()) { local.Add(id); }
            foreach (var tokenClass in TokenClasses)
            {
                compute[ChatMeterId(id, tokenClass)] = UsdRatePerMillion(model, tokenClass) * ComputeUnits.PerUsd;
            }
            compute[EmbeddingMeterId(id)] = UsdRatePerMillion(model, "input") * ComputeUnits.PerUsd;
        }
        _computePerMillion = compute;
        _localModels = local;
    }

    public string Version { get; }

    public static string ChatMeterId(string model, string tokenClass) => $"chat:{model}:{tokenClass}";

    public static string EmbeddingMeterId(string model) => $"embedding:{model}:input";

    public bool IsLocal(string model) => _localModels.Contains(model);

    public decimal PriceInCompute(string meterId, decimal quantity)
    {
        if (string.IsNullOrWhiteSpace(meterId) || quantity <= 0) { return 0m; }
        return _computePerMillion.TryGetValue(meterId, out var computePerMillion)
            ? decimal.Round(computePerMillion * quantity / PerMillionTokens, 6, MidpointRounding.ToEven)
            : 0m;
    }

    private static decimal UsdRatePerMillion(JsonElement model, string tokenClass)
        => model.TryGetProperty(tokenClass, out var rate) && rate.ValueKind is JsonValueKind.Number
            ? rate.GetDecimal()
            : 0m;

    private static Stream OpenEmbedded()
        => typeof(PriceBook).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded price book '{ResourceName}' is missing.");
}
