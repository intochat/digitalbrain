namespace DigitalBrain.Units;

/// <summary>
/// Source of synapse catalogs: provides the static framework catalog and resolves catalogs for dynamic unit state records.
/// </summary>
public interface ISynapseCatalogSource
{
    /// <summary>The static catalog of framework-declared states and functions.</summary>
    SynapseCatalog Static { get; }

    /// <summary>Looks up the synapse catalog declaring the given state record type.</summary>
    SynapseCatalog Of(Type stateRecord);
}
