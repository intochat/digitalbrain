using System.Text.Json;
using DigitalBrain.Abstractions.Behavior;


namespace DigitalBrain.Core.Behavior;

public sealed class BehaviorBuilder
{
    private readonly string _id;
    private readonly string _name;
    private readonly List<BehaviorNode> _nodes = [];
    private readonly List<BehaviorSynapse> _synapses = [];
    private string _trigger = "Run";
    private string? _description;

    private BehaviorBuilder(string id, string name)
    {
        _id = id;
        _name = name;
    }

    public static BehaviorBuilder Define(string id, string name) => new(id, name);

    public BehaviorBuilder On(string trigger)
    {
        _trigger = trigger;
        return this;
    }

    public BehaviorBuilder Describe(string description)
    {
        _description = description;
        return this;
    }

    public BehaviorBuilder Node(string id, string kind, JsonElement config, string inputType = "Json", string outputType = "Json")
    {
        _nodes.Add(new(id, kind, config.Clone(), inputType, outputType));
        return this;
    }

    public BehaviorBuilder Node(string id, string kind, string config = "{}", string inputType = "Json", string outputType = "Json")
    {
        using var document = JsonDocument.Parse(config);
        return Node(id, kind, document.RootElement, inputType, outputType);
    }

    public BehaviorBuilder Connect(string from, string to)
    {
        _synapses.Add(new(from, to));
        return this;
    }

    public BehaviorDefinition Build()
    {
        var definition = new BehaviorDefinition(_id, _name, _trigger, [.. _nodes], [.. _synapses], _description);
        var validation = BehaviorValidator.Validate(definition);
        if (!validation.Valid)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors));
        }
        return definition;
    }
}
