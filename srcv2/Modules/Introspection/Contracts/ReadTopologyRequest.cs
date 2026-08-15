using System.Text.Json.Serialization;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Introspection;

[GenerateSerializer]
[Alias("introspection.read-topology-request")]
public sealed record ReadTopologyRequest : RequestSynapse<TopologyRead>
{
    public ReadTopologyRequest()
        : this(CommandId.New())
    {
    }

    [JsonConstructor]
    public ReadTopologyRequest(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        CommandId = commandId;
    }

    [Id(0)]
    public CommandId CommandId { get; init; }
}

