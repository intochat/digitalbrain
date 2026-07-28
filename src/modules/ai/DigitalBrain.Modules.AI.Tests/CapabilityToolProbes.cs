using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.ModuleTests;

[GenerateSerializer]
[Alias("moduletests.account-enriched-probe")]
public sealed record ProbeAccountEnriched(
    [property: Id(0)] string AccountId,
    [property: Id(1)] string MessageId) : Synapse;

[Alias("DigitalBrain.ModuleTests.IEnrichmentProbe")]
[ClientEntryPoint]
public partial interface IEnrichmentProbe : INeuron
{
    [Alias(nameof(Enrich))]
    Task Enrich(string accountId, string messageId);
}

[Alias("DigitalBrain.ModuleTests.IToolAgentProbe")]
public partial interface IToolAgentProbe : IAgent;

public sealed class EnrichmentProbe : Neuron, IEnrichmentProbe, IEmit<ProbeAccountEnriched>
{
    public Task Enrich(string accountId, string messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        return EmitAsync(new ProbeAccountEnriched(accountId, messageId));
    }
}

public sealed class ToolAgentProbe(
    [FromKeyedServices(typeof(Llama32))] IChatClient chatClient)
    : Agent(chatClient), IToolAgentProbe
{
    public const string EnrichTool = "enrich_account_from_email";
    public const string ProbeName = "enrichment";
    public const string ProbeInstructions =
        "You are the capability seam probe. Use only the supplied tools.";

    protected override string? Instructions => ProbeInstructions;

    protected override IReadOnlyList<CapabilityTool> Tools =>
    [
        Capability(
            EnrichTool,
            "Populate a Salesforce account from a Gmail message.",
            (string accountId, string messageId) => EnrichAsync(accountId, messageId)),
    ];

    private async Task<string> EnrichAsync(string accountId, string messageId)
    {
        var enrichment = GrainFactory.GetGrain<IEnrichmentProbe>(
            NeuronId.For<IEnrichmentProbe>(Id.Owner, ProbeName).ToGrainId());

        await enrichment.Enrich(accountId, messageId);

        return $"enriched {accountId}";
    }
}
