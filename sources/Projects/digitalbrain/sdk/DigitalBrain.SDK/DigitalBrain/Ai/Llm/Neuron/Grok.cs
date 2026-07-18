using DigitalBrain.SDK.DigitalBrain.Security;
using DigitalBrain.SDK.XAI.Grok;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Llm.Neuron;

/// <summary>
/// Dynamic, DPAPI-protected Grok neuron inheriting from Llm.
/// </summary>
[GrainType(NeuronTargetFqn)]
[ImplicitStreamSubscription(nameof(Grok))]
internal sealed class Grok : Llm
{
    public new const string NeuronTargetFqn = "DigitalBrain.Ai.Grok";

    private readonly ISecretVault _vault;

    public Grok(ISecretVault vault) : base()
    {
        _vault = vault;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        string? apiKey = null;
        try
        {
            apiKey = await _vault.DecryptSecretAsync("xai-api-key", cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to decrypt xai-api-key from ISecretVault, falling back.");
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            apiKey = Environment.GetEnvironmentVariable("XAI_API_KEY") ?? "mock-xai-api-key";
        }

        _chat = new GrokConnector(apiKey, "grok-beta");

        // Bypass base class dynamic resolution of keyed chat client
        // but let Orleans and custom properties do their initialization.
        await base.OnActivateAsync(cancellationToken);
        
        // Ensure our custom _chat client is not overwritten by Llm's OnActivateAsync service resolver!
        _chat = new GrokConnector(apiKey, "grok-beta");
    }
}
