using System.Text.Json;

namespace DigitalBrain.V2.Catalog;

[GenerateSerializer]
public enum CatalogKind
{
    Synapse = 0,
    Neuron = 2
}

[GenerateSerializer]
public sealed record CatalogDocument(
    [property: Id(0)] CatalogEntry[] Entries,
    [property: Id(1)] CatalogEdge[] Edges)
{
    public string ToJson() => JsonSerializer.Serialize(this, CatalogJson.Options);

    public string ToConstellationText()
    {
        var lines = Edges
            .OrderBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.Synapse, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal)
            .Select(edge => $"{edge.From} --{edge.Synapse}--> {edge.To}");

        return string.Join(Environment.NewLine, lines);
    }
}

[GenerateSerializer]
public sealed record CatalogEntry(
    [property: Id(0)] string Fqn,
    [property: Id(1)] CatalogKind Kind,
    [property: Id(2)] string[] Fields,
    [property: Id(3)] string[] InEdges,
    [property: Id(4)] string[] OutEdges);

[GenerateSerializer]
public sealed record CatalogEdge(
    [property: Id(0)] string From,
    [property: Id(1)] string Synapse,
    [property: Id(2)] string SynapseFqn,
    [property: Id(3)] string To);

internal static class CatalogJson
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
}
