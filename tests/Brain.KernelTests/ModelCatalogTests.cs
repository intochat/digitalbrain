using Brain.Contracts;
using Brain.Modules.Ai;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Brain.KernelTests;

public class ModelCatalogTests
{
    [Fact]
    public void Parses_tier_from_neuron_id()
    {
        Assert.Equal(ModelTier.Balanced, ModelCatalog.ParseTier("llm/balanced"));
        Assert.Equal(ModelTier.Fast, ModelCatalog.ParseTier("llm/fast"));
    }

    [Fact]
    public void Parses_tier_from_neuron_id_with_replay_suffix()
    {
        Assert.Equal(ModelTier.Balanced, ModelCatalog.ParseTier("llm/balanced-replay"));
    }

    [Fact]
    public void Unknown_tier_fails_closed()
    {
        var exception = Assert.Throws<BrainException>(() => ModelCatalog.ParseTier("llm/galaxy"));
        Assert.Equal("input.invalid", exception.Code);
    }

    [Fact]
    public void Configuration_defaults_bind_all_tiers_to_ollama()
    {
        var config = new ConfigurationBuilder().Build();
        var catalog = ModelCatalog.FromConfiguration(config);
        var binding = catalog.Resolve(ModelTier.Reasoning);
        Assert.Equal("ollama", binding.Provider);
        Assert.Equal("llama3.1:8b", binding.Model);
    }

    [Fact]
    public void Missing_binding_reports_model_unavailable()
    {
        var catalog = new ModelCatalog([new ModelBinding(ModelTier.Fast, "ollama", "x")]);
        var exception = Assert.Throws<BrainException>(() => catalog.Resolve(ModelTier.Reasoning));
        Assert.Equal(BrainErrors.ModelUnavailable, exception.Code);
    }

    [Fact]
    public void Numeric_tier_segment_fails_closed()
    {
        var exception = Assert.Throws<BrainException>(() => ModelCatalog.ParseTier("llm/99"));
        Assert.Equal("input.invalid", exception.Code);
    }
}
