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

    /// <summary>Stages <paramref name="staged"/> under <paramref name="sourceKey"/> for <see cref="ForNeeds"/>, replacing an earlier stage of the key; <see cref="Current"/> is unchanged.</summary>
    /// <param name="sourceKey">The key of the stage, such as a source key.</param>
    /// <param name="staged">The catalog to serve for the aliases it declares.</param>
    void Stage(string sourceKey, SynapseCatalog staged);

    /// <summary>Drops the stage of <paramref name="sourceKey"/>; an unknown key is ignored.</summary>
    /// <param name="sourceKey">The key of the stage.</param>
    void Unstage(string sourceKey);
}
