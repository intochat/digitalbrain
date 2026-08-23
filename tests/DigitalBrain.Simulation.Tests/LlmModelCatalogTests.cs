using DigitalBrain.AI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class LlmModelCatalogTests
{
    [Fact]
    public void LlmModelsHaveUniqueIdsAndMarkers()
    {
        Assert.Distinct(LLMModel.All.Select(static model => model.Id));
        Assert.Distinct(LLMModel.All.Select(static model => model.Marker));
    }

    [Fact]
    public void EmbeddingModelsHaveUniqueIdsAndMarkers()
    {
        Assert.Distinct(EmbeddingModel.All.Select(static model => model.Id));
        Assert.Distinct(EmbeddingModel.All.Select(static model => model.Marker));
    }

    [Fact]
    public void EveryLlmMarkerRoundTripsThroughTheCatalog()
    {
        Assert.All(LLMModel.All, static model =>
        {
            Assert.Same(model.Marker, LLMModel.FindByMarker(model.Marker)!.Marker);
            Assert.Same(model.Marker, LLMModel.FindByMarkerName(model.Marker.Name)!.Marker);
        });
    }

    [Fact]
    public void EveryEmbeddingMarkerRoundTripsThroughTheCatalog()
    {
        Assert.All(EmbeddingModel.All, static model =>
        {
            Assert.Same(model.Marker, EmbeddingModel.FindByMarker(model.Marker)!.Marker);
            Assert.Same(model.Marker, EmbeddingModel.FindByMarkerName(model.Marker.Name)!.Marker);
        });
    }
}
