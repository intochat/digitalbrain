using DigitalBrain.Runtime.Dynamic;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime;
using DigitalBrain.SDK.DigitalBrain.Ai.Models;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Planning;

[ImplicitStreamSubscription(PlannerNeuronType)]
internal sealed class PlannerNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    ILogger<PlannerNeuron> logger,
    [Llm<Gpt5>] IChatClient chat)
    : Neuron(incoming, outgoing, grains, logger),
      IPlanner, INeuronMetadata, IHandle<PlanNeuronRequest>
{
    public const string PlannerNeuronType = nameof(PlannerNeuron);

    public static NeuronId         Id           => new("ai/planner");
    public static string           Icon         => "planner";
    public static NeuronCapability Capabilities => NeuronCapability.Reasoning;

    public const string SystemPrompt = CreatorSystemPrompt.Value;

    protected override async Task HandleSynapseAsync(Synapse s)
    {
        if (s is not PlanNeuronRequest req) return;

        var catalog = Grains.GetGrain<IBrainCatalog>("global");

        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(
                    () => catalog.ListRegisteredAsync(),
                    name: "list_neurons",
                    description: "Lists registered neurons and the synapse types each handles."),
            ],
        };

        var userPrompt = PlannerPrompt.BuildUserPrompt(req);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, userPrompt),
        };

        var response = await chat.GetResponseAsync(messages, options);
        var draft = DraftedNeuron.ParseFromJson(response.Text ?? "");

        await FireSynapseAsync(new PlanNeuronResponse(FeatureText:           draft.FeatureText,
        StepsCode:             draft.StepsCode,
        ImplCode:              draft.ImplCode,
        DisplayName:           draft.DisplayName,
        Icon:                  draft.Icon,
        RequiresCapabilities:  draft.RequiresCapabilities,
        InvocationSynapseType: draft.InvocationSynapseType,
        InvocationPayloadJson: draft.InvocationPayloadJson,
        ResponseSynapseType:   draft.ResponseSynapseType) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: req.CallerNeuronId,
            receiverNeuronType: req.CallerNeuronType ?? "CreatorNeuron",
            timestamp: default
        ) });
    }
}
