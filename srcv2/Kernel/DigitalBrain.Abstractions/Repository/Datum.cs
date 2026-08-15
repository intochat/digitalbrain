namespace DigitalBrain.Abstractions.Repository;

// Carrier for kind-declared facts that are not compiled CLR synapses.
// Effective wire alias for routing is Kind (e.g. "calc.result"), not the carrier alias.
[GenerateSerializer]
[Alias("db.datum")]
public sealed record Datum(
    [property: Id(0)] string Kind,
    [property: Id(1)] Dictionary<string, string> Fields) : Synapse;

