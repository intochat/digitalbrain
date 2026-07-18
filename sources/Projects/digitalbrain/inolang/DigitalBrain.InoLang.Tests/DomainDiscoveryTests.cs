using DigitalBrain.Runtime.User;

namespace DigitalBrain.InoLang.Tests;

public sealed class DomainDiscoveryTests
{
    [Fact]
    public void DomainDiscovery_scans_and_finds_digitalbrain_ino_details()
    {
        var discovery = new DomainDiscovery();
        var results = discovery.Search("DigitalBrain.System");

        results.Should().NotBeNull();
        results.Should().NotBeEmpty();

        var systemResult = results.FirstOrDefault(r => r.Domain == "DigitalBrain.System");
        systemResult.Should().NotBeNull();
        systemResult!.FilePath.Should().Contain("digitalbrain.ino");
        systemResult.Neurons.Should().Contain("DigitalBrain.BrainRegistry");
        systemResult.Neurons.Should().Contain("DigitalBrain.SDK.AspireRuntime");
        systemResult.Synapses.Should().Contain("DigitalBrain.Kernel.Loaded");
    }

    [Fact]
    public void DomainDiscovery_returns_empty_on_blank_or_invalid_search()
    {
        var discovery = new DomainDiscovery();
        
        var resultsEmpty = discovery.Search("");
        resultsEmpty.Should().BeEmpty();

        var resultsWhitespace = discovery.Search("   ");
        resultsWhitespace.Should().BeEmpty();

        var resultsNoMatch = discovery.Search("NonExistentDomainOrNeuronOrSynapseNameXYZ");
        resultsNoMatch.Should().BeEmpty();
    }
}
