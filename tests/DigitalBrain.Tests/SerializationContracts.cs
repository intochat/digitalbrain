using System.Reflection;
using DigitalBrain.Abstractions;
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
            [nameof(CapabilityRequested)] = "db.capability-requested",
            [nameof(CapabilityCompleted)] = "db.capability-completed",
            [nameof(CapabilityFailed)] = "db.capability-failed",
            [nameof(CapabilityRejected)] = "db.capability-rejected",
            [nameof(Synapse)] = "db.synapse",
            [nameof(SynapseDelivery)] = "db.synapse-delivery",
            [nameof(SynapseId)] = "db.synapse-id",
            [nameof(CorrelationId)] = "db.correlation-id",
            [nameof(NeuronId)] = "db.neuron-id",
            [nameof(OwnerId)] = "db.owner-id",
            [nameof(JournalKind)] = "db.journal-kind",
            [nameof(JournalRead)] = "db.journal-read",
            [nameof(JournalSnapshot)] = "db.journal-snapshot",
            [nameof(JournalTally)] = "db.journal-tally",
            [nameof(INeuron)] = "db.neuron",
            [nameof(ISessionNeuron)] = "db.session",
            [nameof(NeuronAuthorizationException)] = "db.authorization-error",
            [nameof(ISubscriptionRegistry)] = "db.subscription-registry",
            [nameof(IJournalObserver)] = "db.journal-observer",
        };

        var declared = Abstractions.GetExportedTypes()
            .Select(type => (type.Name, Alias: type.GetCustomAttributes<AliasAttribute>(inherit: false).FirstOrDefault()?.Alias))
            .Where(entry => entry.Alias is not null)
            .ToDictionary(entry => entry.Name, entry => entry.Alias!, StringComparer.Ordinal);

        Assert.Equal(expected, declared);
    }

    [Fact]
    public void EverySerializableTypeDeclaresGenerateSerializer()
    {
        var aliasedWithoutSerializer = Abstractions.GetExportedTypes()
            .Where(type => type.GetCustomAttributes<AliasAttribute>(inherit: false).Any())
            .Where(type => !type.IsEnum && !type.IsInterface)
            .Where(type => type.GetCustomAttribute<GenerateSerializerAttribute>(inherit: false) is null)
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(aliasedWithoutSerializer);
    }

    [Fact]
    public void SynapseIsAThinRecordWithNoFrameworkPayloadMembers()
    {
        var serializedMembers = typeof(Synapse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttribute<IdAttribute>() is not null)
            .Select(property => property.Name)
            .ToList();

        Assert.Empty(serializedMembers);
    }

    [Fact]
    public void DeliveryEnvelopeCarriesMetadataOutsideTheSynapse()
    {
        var serializedMembers = typeof(SynapseDelivery)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttribute<IdAttribute>() is not null)
            .Select(property => property.Name)
            .ToList();

        Assert.Equal(
            [
                nameof(SynapseDelivery.Synapse),
                nameof(SynapseDelivery.SynapseId),
                nameof(SynapseDelivery.CorrelationId),
                nameof(SynapseDelivery.CausationId),
                nameof(SynapseDelivery.Caller),
                nameof(SynapseDelivery.Sequence),
                nameof(SynapseDelivery.Timestamp),
            ],
            serializedMembers);
    }

    [Fact]
    public void DeliveryEnvelopeHasNoPublicConstructorOrMetadataSetters()
    {
        Assert.Empty(typeof(SynapseDelivery).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        var publicSetters = typeof(SynapseDelivery)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod?.IsPublic is true)
            .Select(property => property.Name);

        Assert.Empty(publicSetters);
    }

    [Fact(DisplayName = "generic capability facts carry protocol metadata and no domain payload")]
    public void CapabilityFactsCarryNoDomainPayload()
    {
        Assert.Equal(
            [nameof(CapabilityRequested.Contract), nameof(CapabilityRequested.Method), nameof(CapabilityRequested.Target)],
            PublicPropertiesDeclaredBy<CapabilityRequested>());
        Assert.Equal([nameof(CapabilityCompleted.Request)], PublicPropertiesDeclaredBy<CapabilityCompleted>());
        Assert.Equal([nameof(CapabilityFailed.Request)], PublicPropertiesDeclaredBy<CapabilityFailed>());
        Assert.Equal([nameof(CapabilityRejected.Request)], PublicPropertiesDeclaredBy<CapabilityRejected>());
        Assert.Null(Abstractions.GetType("DigitalBrain.Abstractions.CapabilityCall"));
    }

    private static string[] PublicPropertiesDeclaredBy<T>()
        => typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .ToArray();
}
