using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using Microsoft.Agents.AI;

namespace DigitalBrain.AI;

internal static class MafAgentFactory
{
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The adapter owns no disposable resource; it only forwards to an Orleans grain proxy for the lifetime of the MAF agent.")]
    internal static AIAgent Create(
        ILLM model,
        string instructions,
        IReadOnlyList<INeuron> capabilities)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        ArgumentNullException.ThrowIfNull(capabilities);

        foreach (var capability in capabilities)
        {
            ArgumentNullException.ThrowIfNull(capability);
        }

        return new ChatClientAgent(
            new NeuronChatClient(model),
            instructions: instructions);
    }
}
