using DigitalBrain.Protocol;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.UI;

namespace DigitalBrain.Os.Tests;

public interface IExperienceDemoHandler : INeuron, IHandle<BundleInstalled>;

[GrainType("experience-demo-handler")]
public sealed class DemoExperienceHandler : SimulationNeuron, IExperienceDemoHandler
{
    public static readonly List<string> ReceivedExperiencesForTest = new();

    public Task HandleAsync(BundleInstalled synapse, CancellationToken cancellationToken)
    {
        ReceivedExperiencesForTest.Add(synapse.BundleId);
        return Task.CompletedTask;
    }
}

// Weather watcher demo: the "new handler" brought by a weather-watcher bundle created via ino/LLM.
// Reacts to BundleInstalled (the broadcast after ship+install from another kernel).
// In real use, the weather watcher bundle enables https weather searches (via LlmAgentNeuron.web_get tool).
// For sim coverage of 2-kernel case, this collects to prove the installed bundle's handler reacted on the receiving kernel.
public interface IWeatherWatcherDemo : INeuron, IHandle<BundleInstalled>;

[GrainType("weather-watcher-demo")]
public sealed class WeatherWatcherDemo : SimulationNeuron, IWeatherWatcherDemo
{
    public static readonly List<string> ReceivedExperiencesForTest = new();

    public Task HandleAsync(BundleInstalled synapse, CancellationToken cancellationToken)
    {
        ReceivedExperiencesForTest.Add(synapse.BundleId);
        // Simulate the https search reaction for the weather agent (the real https is wired in LlmAgentNeuron web_get).
        // Emitting telemetry makes the "reaction to broadcasting" observable on timeline for the second kernel.
        return Task.CompletedTask;
    }
}

public interface ISurfaceCollector : INeuron, IHandle<UiSurface>;

[GrainType("surface-collector")]
public sealed class SurfaceCollector : SimulationNeuron, ISurfaceCollector
{
    public static readonly List<UiSurface> CollectedForTest = new();

    public Task HandleAsync(UiSurface synapse, CancellationToken cancellationToken)
    {
        CollectedForTest.Add(synapse);
        return Task.CompletedTask;
    }
}

// Private contract-only simulation double (for contract distribution BDD).
// Declares IHandle for a synapse type that is "new" from the contract perspective.
// The contract bundle ships only the shape (names); the test asm supplies the concrete Synapse + handler impl (simulation double).
// Pre-activated via Given in bindings so it subscribes on timeline; after contract install on target brain (e.g. account-b),
// ListSubscribers grows (via contract contrib) and sending the synapse delivers via normal stream + SynapseDispatch (manifest or reflection).
[GenerateSerializer]
public sealed record ReviewRequest(string Target, string Content) : Synapse;

public interface IPrivateReviewContractNeuron : INeuron, IHandle<ReviewRequest>;

[GrainType("private-review-simulation")]
public sealed class PrivateReviewSimulation : SimulationNeuron, IPrivateReviewContractNeuron
{
    public static readonly List<string> ReceivedForTest = new();

    public Task HandleAsync(ReviewRequest synapse, CancellationToken cancellationToken)
    {
        ReceivedForTest.Add(synapse.Target);
        return Task.CompletedTask;
    }
}
