using System.Text.Json.Serialization;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Introspection;

[GenerateSerializer]
[Alias("introspection.tally-journal-request")]
public sealed record TallyJournalRequest : RequestSynapse<JournalTallied>
{
    public TallyJournalRequest(string neuronType, string neuronName)
        : this(neuronType, neuronName, JournalDirection.Outgoing, CommandId.New())
    {
    }

    public TallyJournalRequest(string neuronType, string neuronName, string direction, CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        NeuronType = IntrospectionIdentity.Validated(neuronType, nameof(neuronType));
        NeuronName = IntrospectionIdentity.Validated(neuronName, nameof(neuronName));
        Direction = JournalDirection.Validated(direction, nameof(direction));
        CommandId = commandId;
    }

    [JsonConstructor]
    public TallyJournalRequest(
        string neuronType,
        string neuronName,
        CommandId commandId,
        string direction = JournalDirection.Outgoing)
        : this(neuronType, neuronName, direction, commandId)
    {
    }

    [Id(0)]
    public string NeuronType { get; init; }

    [Id(1)]
    public string NeuronName { get; init; }

    [Id(2)]
    public string Direction { get; init; }

    [Id(3)]
    public CommandId CommandId { get; init; }

    [JsonIgnore]
    public JournalKind Kind => JournalDirection.Parse(Direction);
}

