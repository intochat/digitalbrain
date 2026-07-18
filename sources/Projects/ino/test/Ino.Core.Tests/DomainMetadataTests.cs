using Xunit;

namespace Ino.Core.Tests;

public sealed class DomainMetadataTests
{
    [Fact]
    public void Metadata_CarriesAllFields()
    {
        var metadata = new DomainMetadata(
            NeuronId: "Ino.Domains.Travel.TripPlanner",
            Version: "1.0.0",
            Description: "Plan trips with flights, hotels, and activities.",
            Keywords: new[] { "travel", "trip", "flight" },
            CanonicalNeurons: new[]
            {
                new CanonicalNeuronInfo(
                    SynapseType: "Ino.Domains.Travel.TripPlanner.Contracts.PlanTrip",
                    GrainType: "Ino.Domains.Travel.TripPlanner.TripPlanner",
                    IsUserEntry: true)
            },
            ReactiveNeurons: Array.Empty<ReactiveNeuronInfo>(),
            UserEntrySchemas: new[] { "Ino.Domains.Travel.TripPlanner.Contracts.PlanTrip" },
            RequiredCapabilities: new Capability[]
            {
                new Capability.Llm(LlmTier.Reasoning),
                new Capability.Persistence("trip-planner")
            },
            CoreVersion: "0.1.0");

        Assert.Equal("Ino.Domains.Travel.TripPlanner", metadata.NeuronId);
        Assert.Equal("1.0.0", metadata.Version);
        Assert.Equal("Plan trips with flights, hotels, and activities.", metadata.Description);
        Assert.Equal(new[] { "travel", "trip", "flight" }, metadata.Keywords);
        var canonical = Assert.Single(metadata.CanonicalNeurons);
        Assert.True(canonical.IsUserEntry);
        Assert.Equal("Ino.Domains.Travel.TripPlanner.Contracts.PlanTrip", canonical.SynapseType);
        Assert.Equal("Ino.Domains.Travel.TripPlanner.TripPlanner", canonical.GrainType);
        Assert.Empty(metadata.ReactiveNeurons);
        var onlySchema = Assert.Single(metadata.UserEntrySchemas);
        Assert.Equal("Ino.Domains.Travel.TripPlanner.Contracts.PlanTrip", onlySchema);
        Assert.Single(metadata.RequiredCapabilities.OfType<Capability.Llm>(), llm => llm.Tier == LlmTier.Reasoning);
        Assert.Single(metadata.RequiredCapabilities.OfType<Capability.Persistence>(), p => p.StoragePrefix == "trip-planner");
        Assert.Equal("0.1.0", metadata.CoreVersion);
    }
}
