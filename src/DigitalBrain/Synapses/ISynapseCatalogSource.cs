namespace DigitalBrain;

/// <summary>The live synapse catalog of a host: neurons read <see cref="Current"/> on each turn, so a change reaches active neurons.</summary>
public interface ISynapseCatalogSource
{
    /// <summary>The catalog in force now.</summary>
    SynapseCatalog Current { get; }

    /// <summary>Bumped on every change of <see cref="Current"/>.</summary>
    long Version { get; }

    /// <summary>Raised with the new catalog after <see cref="Current"/> changed.</summary>
    event Action<SynapseCatalog>? Changed;

    /// <summary>
    /// The catalog an install of <paramref name="alias"/> derives its needs from: a staged catalog declaring the alias when one is
    /// staged, else <see cref="Current"/>.
    /// </summary>
    /// <param name="alias">The state alias.</param>
    /// <returns>The catalog.</returns>
    SynapseCatalog ForNeeds(string alias);
}
