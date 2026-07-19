using System.Reflection;
using DigitalBrain;
using Orleans;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class SerializationContracts
{
    private static readonly Assembly Abstractions = typeof(Synapse).Assembly;

    [Fact]
    public void PinnedAliasesNeverChange()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(Synapse)] = "db.synapse",
            [nameof(SynapseMetadata)] = "db.synapse-metadata",
            [nameof(SynapseId)] = "db.synapse-id",
            [nameof(CorrelationId)] = "db.correlation-id",
            [nameof(NeuronId)] = "db.neuron-id",
            [nameof(OwnerId)] = "db.owner-id",
            [nameof(RoutingMode)] = "db.routing-mode",
            [nameof(JournalKind)] = "db.journal-kind",
            [nameof(INeuron)] = "db.neuron",
            [nameof(NeuronAuthorizationException)] = "db.authorization-error",
            [nameof(SynapseDepthExceededException)] = "db.depth-error",
            [nameof(ISubscriptionRegistry)] = "db.subscription-registry",
        };

        var declared = Abstractions.GetExportedTypes()
            .Select(type => (type.Name, Alias: type.GetCustomAttribute<AliasAttribute>()?.Alias))
            .Where(entry => entry.Alias is not null)
            .ToDictionary(entry => entry.Name, entry => entry.Alias!, StringComparer.Ordinal);

        Assert.Equal(expected, declared);
    }

    [Fact]
    public void EverySerializableTypeDeclaresGenerateSerializer()
    {
        var aliasedWithoutSerializer = Abstractions.GetExportedTypes()
            .Where(type => type.GetCustomAttribute<AliasAttribute>() is not null)
            .Where(type => !type.IsEnum && !type.IsInterface)
            .Where(type => type.GetCustomAttribute<GenerateSerializerAttribute>() is null)
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(aliasedWithoutSerializer);
    }

    [Fact]
    public void SynapseCarriesMetadataAsItsOnlySerializedMember()
    {
        var serializedMembers = typeof(Synapse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttribute<IdAttribute>() is not null)
            .Select(property => property.Name)
            .ToList();

        Assert.Equal([nameof(Synapse.Metadata)], serializedMembers);
    }

    [Fact]
    public void UnstampedSynapseFailsLoudlyInsteadOfReturningEmptyLineage()
    {
        var unstamped = new SerializationProbe();

        Assert.Throws<InvalidOperationException>(() => unstamped.Stamped);
    }

    private sealed record SerializationProbe : Synapse;
}
