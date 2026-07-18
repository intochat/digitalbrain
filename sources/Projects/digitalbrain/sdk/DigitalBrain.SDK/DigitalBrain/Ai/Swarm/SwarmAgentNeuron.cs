using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Swarm;

public interface ISwarmAgentNeuron : INeuron
{
    Task RegisterSessionAsync(Guid sessionId);
}

[ImplicitStreamSubscription(nameof(SwarmAgentNeuron))]
internal sealed class SwarmAgentNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    ILogger<SwarmAgentNeuron> logger,
    IServiceProvider services)
    : Neuron(incoming, outgoing, grains, logger),
      ISwarmAgentNeuron,
      INeuronMetadata,
      IHandle<SwarmDocumentAssigned>,
      IHandle<SwarmAgentMessage>
{
    public static NeuronId Id => new("swarm/agent-worker");
    public static string Icon => "worker";
    public static NeuronCapability Capabilities => NeuronCapability.Reasoning;

    protected override async Task HandleSynapseAsync(Synapse synapse)
    {
        if (synapse is SwarmDocumentAssigned or SwarmAgentMessage)
        {
            await DispatchSynapseAsync(synapse);
        }
    }

    public async Task RegisterSessionAsync(Guid sessionId)
    {
        Logger.LogInformation("SwarmAgentNeuron registering stream subscription for Session: {SessionId}", sessionId);

        var streamProvider = this.GetStreamProvider("synapse-streams");
        var stream = streamProvider.GetStream<Synapse>(StreamId.Create("swarm-session", sessionId));
        await stream.SubscribeAsync(this);
    }

    public async Task HandleAsync(SwarmDocumentAssigned assignment, CancellationToken ct)
    {
        if (assignment.ReceiverNeuronId != InstanceId)
        {
            return;
        }

        Logger.LogInformation("SwarmAgentNeuron {InstanceId} starting review of document: {File}", 
            InstanceId, assignment.DocumentName);

        // 1. Check for registered IChatClient to run real AI review
        IChatClient? chatClient = null;
        foreach (var model in global::DigitalBrain.SDK.DigitalBrain.Ai.Models.LlmModel.All)
        {
            var client = services.GetKeyedService<IChatClient>(model.ServiceKey);
            if (client != null)
            {
                if (chatClient == null || model.Provider == "grok")
                {
                    chatClient = client;
                }
            }
        }

        string findingMessage = string.Empty;
        int lineNumber = 1;
        string severity = "info";

        if (chatClient != null)
        {
            Logger.LogInformation("SwarmAgentNeuron resolved IChatClient from keyed services.");
            try
            {
                var response = await chatClient.GetResponseAsync(
                    $"Please review this C# source file '{assignment.DocumentName}' and identify one critical issue or suggest one refactoring:\n\n{assignment.SourceCode}",
                    cancellationToken: ct);
                findingMessage = response.Text ?? "No issue detected by AI.";
                severity = "warning";
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to call IChatClient, falling back to Roslyn analysis.");
            }
        }

        // 2. Roslyn fallback analysis if AI result is empty
        if (string.IsNullOrEmpty(findingMessage))
        {
            var workspace = new SwarmWorkspace();
            workspace.AddOrUpdateDocument(assignment.DocumentName, assignment.SourceCode);
            
            var undocumentedTypes = workspace.FindUndocumentedTypes();
            var undocumentedMethods = workspace.FindUndocumentedMethods();

            if (undocumentedTypes.Count > 0)
            {
                var type = undocumentedTypes.First();
                lineNumber = type.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                findingMessage = $"Public type '{type.Identifier.Text}' lacks documentation comments or XML docstrings.";
                severity = "warning";
            }
            else if (undocumentedMethods.Count > 0)
            {
                var method = undocumentedMethods.First();
                lineNumber = method.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                findingMessage = $"Public method '{method.Identifier.Text}' lacks documentation comments or XML docstrings.";
                severity = "info";
            }
            else
            {
                findingMessage = "Code conforms perfectly to documentation standards (Roslyn parsed successfully).";
                severity = "info";
            }
        }

        // 3. Communicate findings to peers (simulate communication in swarm)
        var streamProvider = this.GetStreamProvider("synapse-streams");
        var stream = streamProvider.GetStream<Synapse>(StreamId.Create("swarm-session", assignment.SessionId));

        var msg = new SwarmAgentMessage(SessionId: assignment.SessionId,
        SenderName: $"Agent-{InstanceId.ToString()[..4]}",
        MessageContent: $"Completed review of {assignment.DocumentName}. Found severity '{severity}': {findingMessage}") { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: assignment.CorrelationId,
            causationId: assignment.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(SwarmAgentNeuron),
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: nameof(SwarmAgentNeuron),
            timestamp: DateTimeOffset.UtcNow
        ) };
        await stream.OnNextAsync(msg);

        // 4. Fire the proposed finding synapse
        var proposed = new SwarmFindingProposed(SessionId: assignment.SessionId,
        DocumentName: assignment.DocumentName,
        Severity: severity,
        FindingMessage: findingMessage,
        LineNumber: lineNumber) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: assignment.CorrelationId,
            causationId: assignment.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: nameof(SwarmAgentNeuron),
            receiverNeuronId: assignment.CallerNeuronId,
            receiverNeuronType: assignment.CallerNeuronType ?? nameof(SwarmSessionNeuron),
            timestamp: DateTimeOffset.UtcNow
        ) };
        await stream.OnNextAsync(proposed);
    }

    public Task HandleAsync(SwarmAgentMessage message, CancellationToken ct)
    {
        if (message.CallerNeuronId == InstanceId)
        {
            return Task.CompletedTask;
        }

        Logger.LogInformation("SwarmAgent {InstanceId} heard peer message from '{Sender}': {Content}", 
            InstanceId, message.SenderName, message.MessageContent);
        return Task.CompletedTask;
    }
}
