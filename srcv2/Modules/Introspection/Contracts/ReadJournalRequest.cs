using System.Text.Json.Serialization;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Introspection;

[GenerateSerializer]
[Alias("introspection.read-journal-request")]
public sealed record ReadJournalRequest : RequestSynapse<JournalPageRead>
{
    public const int DefaultMaxEntries = 50;
    public const int MinimumMaxEntries = 1;
    public const int MaximumMaxEntries = 200;

    public ReadJournalRequest(string neuronType, string neuronName)
        : this(neuronType, neuronName, JournalDirection.Outgoing, afterSequence: 0, DefaultMaxEntries, CommandId.New())
    {
    }

    public ReadJournalRequest(
        string neuronType,
        string neuronName,
        string direction,
        long afterSequence,
        int maxEntries,
        CommandId commandId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);
        if (maxEntries is < MinimumMaxEntries or > MaximumMaxEntries)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxEntries),
                maxEntries,
                $"A journal page holds between {MinimumMaxEntries} and {MaximumMaxEntries} entries.");
        }

        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        NeuronType = IntrospectionIdentity.Validated(neuronType, nameof(neuronType));
        NeuronName = IntrospectionIdentity.Validated(neuronName, nameof(neuronName));
        Direction = JournalDirection.Validated(direction, nameof(direction));
        AfterSequence = afterSequence;
        MaxEntries = maxEntries;
        CommandId = commandId;
    }

    [JsonConstructor]
    public ReadJournalRequest(
        string neuronType,
        string neuronName,
        CommandId commandId,
        string direction = JournalDirection.Outgoing,
        long afterSequence = 0,
        int maxEntries = DefaultMaxEntries)
        : this(neuronType, neuronName, direction, afterSequence, maxEntries, commandId)
    {
    }

    [Id(0)]
    public string NeuronType { get; init; }

    [Id(1)]
    public string NeuronName { get; init; }

    [Id(2)]
    public string Direction { get; init; }

    [Id(3)]
    public long AfterSequence { get; init; }

    [Id(4)]
    public int MaxEntries { get; init; }

    [Id(5)]
    public CommandId CommandId { get; init; }

    [JsonIgnore]
    public JournalKind Kind => JournalDirection.Parse(Direction);
}

