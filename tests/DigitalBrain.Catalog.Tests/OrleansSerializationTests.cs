using System.Collections.ObjectModel;
using System.Reflection;
using DigitalBrain.Abstractions.Identity;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Xunit;

namespace DigitalBrain.Catalog.Tests;

public sealed class OrleansSerializationTests
{
    [Fact]
    public void DeserializedDiscoveryQueryMustCrossAnExplicitNormalizationBoundary()
    {
        using var services = SerializerServices();
        var serializer = services.GetRequiredService<Serializer<DiscoveryQuery>>();
        var malformed = new DiscoveryQuery(
            "fixture", null, null, null, CatalogAvailabilityRequirement.Any, 1, null);
        SetBackingField(malformed, nameof(DiscoveryQuery.Limit), 0);

        var copy = serializer.Deserialize(serializer.SerializeToArray(malformed));

        Assert.Equal(0, copy.Limit);
        Assert.Throws<ArgumentOutOfRangeException>(copy.Normalize);
    }

    [Fact]
    public void OrleansRoundTripPreservesCanonicalCollectionsAsReadOnly()
    {
        using var services = SerializerServices();
        var querySerializer = services.GetRequiredService<Serializer<DiscoveryQuery>>();
        var contributionSerializer = services.GetRequiredService<Serializer<CatalogContribution>>();
        var query = new DiscoveryQuery(
            "fixture",
            [CatalogEntryKind.Operation, CatalogEntryKind.Module],
            ["time", "timer"],
            null,
            CatalogAvailabilityRequirement.Any,
            2,
            null);
        var descriptor = CatalogFixtures.ModuleDescriptor() with
        {
            Discovery = new CatalogDiscoveryText(
                ["fixture"], null, null, null, null, null, null),
        };
        var contribution = new CatalogContribution("Fixture.Module", [descriptor]);

        var queryCopy = querySerializer.Deserialize(querySerializer.SerializeToArray(query));
        var contributionCopy = contributionSerializer.Deserialize(
            contributionSerializer.SerializeToArray(contribution));

        Assert.IsType<ReadOnlyCollection<CatalogEntryKind>>(queryCopy.Kinds);
        Assert.IsType<ReadOnlyCollection<string>>(queryCopy.RequiredTags);
        Assert.IsType<ReadOnlyCollection<CatalogDescriptor>>(contributionCopy.Descriptors);
        Assert.IsType<ReadOnlyCollection<string>>(contributionCopy.Descriptors[0].Discovery.Aliases);
        var normalized = queryCopy.Normalize();
        Assert.Equal(query.Text, normalized.Text);
        Assert.Equal(query.Kinds, normalized.Kinds);
        Assert.Equal(query.RequiredTags, normalized.RequiredTags);
        Assert.Equal(query.Limit, normalized.Limit);
    }

    private static ServiceProvider SerializerServices()
    {
        var services = new ServiceCollection();
        services.AddSerializer(builder => builder
            .AddAssembly(typeof(DiscoveryQuery).Assembly)
            .AddAssembly(typeof(OwnerId).Assembly));
        return services.BuildServiceProvider();
    }

    private static void SetBackingField<T>(T instance, string propertyName, object? value)
        where T : class
    {
        var field = typeof(T).GetField(
            $"<{propertyName}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(instance, value);
    }
}
