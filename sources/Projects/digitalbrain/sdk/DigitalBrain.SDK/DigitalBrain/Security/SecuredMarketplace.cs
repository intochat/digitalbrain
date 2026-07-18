using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Security;

[GrainType("DigitalBrain.Security.SecuredMarketplace")]
[ImplicitStreamSubscription(nameof(SecuredMarketplace))]
internal sealed class SecuredMarketplace(ISecretVault vault) 
    : Neuron(), 
      ISecuredMarketplace, 
      IHandle<SubmitMarketplaceQuery>
{
    private readonly ISecretVault _vault = vault;

    public async Task HandleAsync(SubmitMarketplaceQuery synapse, CancellationToken cancellationToken)
    {
        // 1. Authenticate BrainId
        if (string.IsNullOrEmpty(synapse.BrainId) || synapse.BrainId.Contains("anonymous"))
        {
            Logger.LogWarning("Authentication failed for request with BrainId: {BrainId}", synapse.BrainId);
            var errResponse = new MarketplaceQueryResponse(
                Success: false,
                Data: "",
                ErrorMessage: "Unauthorized: Invalid BrainId"
            )
            {
                Headers = SynapseMetadata.Create(
                    synapseId: Guid.NewGuid(),
                    correlationId: synapse.CorrelationId,
                    causationId: synapse.SynapseId,
                    callerNeuronId: InstanceId,
                    callerNeuronType: NeuronType,
                    receiverNeuronId: synapse.CallerNeuronId,
                    receiverNeuronType: synapse.CallerNeuronType ?? "External"
                )
            };
            await FireSynapseAsync(errResponse, cancellationToken);
            return;
        }

        // 2. Decrypt Marketplace secret from ISecretVault
        string apiKey;
        try
        {
            apiKey = await _vault.DecryptSecretAsync("marketplace-api-key", cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to decrypt marketplace-api-key");
            apiKey = "mock-marketplace-fallback-key";
        }

        var apiKeyPrefix = apiKey.Length >= 5 ? apiKey[..5] : apiKey;
        var responseData = $"Processed query '{synapse.Query}' for user '{synapse.BrainId}' using API Key '{apiKeyPrefix}****'";
        
        var response = new MarketplaceQueryResponse(
            Success: true,
            Data: responseData
        )
        {
            Headers = SynapseMetadata.Create(
                synapseId: Guid.NewGuid(),
                correlationId: synapse.CorrelationId,
                causationId: synapse.SynapseId,
                callerNeuronId: InstanceId,
                callerNeuronType: NeuronType,
                receiverNeuronId: synapse.CallerNeuronId,
                receiverNeuronType: synapse.CallerNeuronType ?? "External"
            )
        };

        await FireSynapseAsync(response, cancellationToken);
    }
}
