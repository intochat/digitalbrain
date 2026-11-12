using System.Text.Json.Serialization;
using DigitalBrain.Contracts;
using DigitalBrain.Neurons;

namespace DigitalBrain.Units;

/// <summary>An alias ownership conflict where an alias is already claimed by another source key.</summary>
/// <param name="Alias">The conflicting state alias.</param>
/// <param name="Owner">The current owning source key.</param>
public sealed record AliasConflict(string Alias, string Owner);

/// <summary>The alias ownership directory of one brain, at {brain}/synapse.sources/default.</summary>
[NeuronAlias("synapse.sources")]
public interface ISources
{
    /// <summary>Claims aliases for a source key. Returns conflict if an alias is already owned by another key.</summary>
    Task<AliasConflict?> Claim(string sourceKey, IReadOnlyList<string> aliases, CancellationToken ct = default);

    /// <summary>Releases aliases previously owned by a source key.</summary>
    Task Release(string sourceKey, IReadOnlyList<string> aliases, CancellationToken ct = default);

    /// <summary>Looks up the source key owning the given state alias.</summary>
    [ReadOnlyCall]
    Task<string?> OwnerOf(string alias, CancellationToken ct = default);

    /// <summary>Returns all deployed source keys of this brain.</summary>
    [ReadOnlyCall]
    Task<IReadOnlyList<string>> Keys(CancellationToken ct = default);
}

/// <summary>The state of the sources neuron: maps alias -> owner source key.</summary>
public sealed record SourcesState
{
    /// <summary>Alias to owning source key.</summary>
    public IReadOnlyDictionary<string, string> Owners { get; init; }

    /// <summary>Initial empty state.</summary>
    public SourcesState() : this(new Dictionary<string, string>(StringComparer.Ordinal)) { }

    /// <summary>Constructor initialized with owners.</summary>
    [JsonConstructor]
    public SourcesState(IReadOnlyDictionary<string, string>? owners)
    {
        Owners = owners ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>Deconstructs the sources state.</summary>
    public void Deconstruct(out IReadOnlyDictionary<string, string> owners)
    {
        owners = Owners;
    }
}

/// <summary>Directory neuron keeping track of alias ownership across unit deployments.</summary>
[Neuron("synapse.sources")]
public sealed class SourcesNeuron : Neuron<SourcesState>, ISources
{
    /// <inheritdoc />
    public async Task<AliasConflict?> Claim(string sourceKey, IReadOnlyList<string> aliases, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourceKey);
        ArgumentNullException.ThrowIfNull(aliases);

        foreach (var alias in aliases)
        {
            if (State.Owners.TryGetValue(alias, out var existingOwner) && !string.Equals(existingOwner, sourceKey, StringComparison.Ordinal))
            {
                return new AliasConflict(alias, existingOwner);
            }
        }

        var nextOwners = new Dictionary<string, string>(State.Owners, StringComparer.Ordinal);
        var newlyClaimed = new List<string>();
        foreach (var alias in aliases)
        {
            if (!nextOwners.ContainsKey(alias))
            {
                newlyClaimed.Add(alias);
            }
            nextOwners[alias] = sourceKey;
        }

        if (newlyClaimed.Count > 0)
        {
            await Save(State with { Owners = nextOwners }, [new AliasesClaimed(sourceKey, newlyClaimed)]).ConfigureAwait(true);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task Release(string sourceKey, IReadOnlyList<string> aliases, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourceKey);
        ArgumentNullException.ThrowIfNull(aliases);

        var nextOwners = new Dictionary<string, string>(State.Owners, StringComparer.Ordinal);
        var released = new List<string>();
        foreach (var alias in aliases)
        {
            if (nextOwners.TryGetValue(alias, out var owner) && string.Equals(owner, sourceKey, StringComparison.Ordinal))
            {
                nextOwners.Remove(alias);
                released.Add(alias);
            }
        }

        if (released.Count > 0)
        {
            await Save(State with { Owners = nextOwners }, [new AliasesReleased(sourceKey, released)]).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    [ReadOnlyCall]
    public Task<string?> OwnerOf(string alias, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(alias);
        return Task.FromResult(State.Owners.TryGetValue(alias, out var owner) ? owner : null);
    }

    /// <inheritdoc />
    [ReadOnlyCall]
    public Task<IReadOnlyList<string>> Keys(CancellationToken ct = default)
    {
        var keys = State.Owners.Values.Distinct(StringComparer.Ordinal);
        return Task.FromResult<IReadOnlyList<string>>([.. keys.OrderBy(k => k, StringComparer.Ordinal)]);
    }
}
