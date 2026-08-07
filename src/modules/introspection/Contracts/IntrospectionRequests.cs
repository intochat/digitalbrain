using System.ComponentModel;
using System.Text.Json.Serialization;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Introspection;

internal static class IntrospectionIdentity
{
    private const char GrainKeySeparator = '/';

    internal static string Validated(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var trimmed = value.Trim();
        if (trimmed.Contains(GrainKeySeparator, StringComparison.Ordinal) || trimmed.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                $"A neuron identity part cannot contain '{GrainKeySeparator}' or whitespace; "
                + $"'{value}' is not addressable.",
                parameterName);
        }

        return trimmed;
    }
}

[GenerateSerializer]
[Alias("introspection.tally-journal-request")]
[Description(
    "Counts journaled synapses by synapse kinds for one neuron of the owning identity, "
    + "answering how often a conversation recorded owner messages")]
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
    private TallyJournalRequest(
        string neuronType,
        string neuronName,
        CommandId commandId,
        string direction = JournalDirection.Outgoing)
        : this(neuronType, neuronName, direction, commandId)
    {
    }

    [Id(0)]
    [Description("Grain type of the neuron to tally, for example 'chat' or 'shell'")]
    public string NeuronType { get; init; }

    [Id(1)]
    [Description("Instance name of the neuron to tally, for example 'main'")]
    public string NeuronName { get; init; }

    [Id(2)]
    [Description("Journal direction: 'incoming' or 'outgoing'. Facts a neuron produced live in its outgoing journal")]
    public string Direction { get; init; }

    [Id(3)]
    public CommandId CommandId { get; init; }

    [JsonIgnore]
    public JournalKind Kind => JournalDirection.Parse(Direction);
}

[GenerateSerializer]
[Alias("introspection.read-journal-request")]
[Description(
    "Returns journaled causal facts for one neuron of the owning identity, bounded by cursor "
    + "and limit; entries record synapse kinds and lineage, excluding argument and payload values")]
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
    private ReadJournalRequest(
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
    [Description("Grain type of the neuron to read, for example 'chat' or 'shell'")]
    public string NeuronType { get; init; }

    [Id(1)]
    [Description("Instance name of the neuron to read, for example 'main'")]
    public string NeuronName { get; init; }

    [Id(2)]
    [Description("Journal direction: 'incoming' or 'outgoing'")]
    public string Direction { get; init; }

    [Id(3)]
    [Description("Resume after this journal sequence; 0 starts at the earliest retained entry")]
    public long AfterSequence { get; init; }

    [Id(4)]
    [Description("Most entries to return, between 1 and 200")]
    public int MaxEntries { get; init; }

    [Id(5)]
    public CommandId CommandId { get; init; }

    [JsonIgnore]
    public JournalKind Kind => JournalDirection.Parse(Direction);
}

[GenerateSerializer]
[Alias("introspection.read-topology-request")]
[Description(
    "Reports the runtime topology of the owning identity: modules the deployment composed and "
    + "neurons currently activated")]
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
