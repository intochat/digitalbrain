using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Sdk.Integrations;
using DigitalBrain.Platform.Integrations;
using Microsoft.Extensions.Options;

namespace DigitalBrain.AI;

// Reads registrations straight from their grains on every derivation: a capability appears the moment
// its provider becomes Ready and disappears the moment it stops, with no cache to go stale.
internal sealed class AiModelCapabilities(IGrainFactory grains, IOptionsMonitor<AIOptions> options) : ICapabilitySource
{
    public const string LlmKind = "llm";

    public async Task<Capability[]> Derive(CallerContext caller, CancellationToken ct)
    {
        var configuration = options.CurrentValue;
        var capabilities = new List<Capability>();
        foreach (var provider in LLMModel.All.Select(model => model.Provider).Distinct())
        {
            var integrationId = provider == AiProvider.Ollama ? "ollama" : AiIntegrations.IdOf(provider);
            if (provider != AiProvider.Ollama)
            {
                var snapshot = await grains.GetGrain<IIntegrationRegistration>("integration/" + integrationId).Read().WaitAsync(ct);
                if (snapshot.Status != RegistrationStatus.Ready) { continue; }
            }
            else if (string.IsNullOrEmpty(configuration.Ollama.Endpoint)) { continue; }

            capabilities.AddRange(LLMModel.All
                .Where(model => model.Provider == provider)
                // A local server being configured does not imply every compiled local model is installed.
                .Where(model => provider != AiProvider.Ollama || configuration.Ollama.Models.ContainsKey(model.Marker.Name))
                .Select(model => new Capability(integrationId, LlmKind, model.Marker.Name, model.Id)));
        }

        return [.. capabilities];
    }
}
