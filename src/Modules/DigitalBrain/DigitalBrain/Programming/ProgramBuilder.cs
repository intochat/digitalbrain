using System.Text.Json;
using DigitalBrain.Abstractions.Programming;

namespace DigitalBrain.Core.Programming;

public sealed class ProgramBuilder
{
    private readonly string _id;
    private readonly string _name;
    private readonly List<ProgramNode> _nodes = [];
    private readonly List<ProgramSynapse> _synapses = [];
    private string _trigger = "Run";
    private string? _description;

    private ProgramBuilder(string id, string name)
    {
        _id = id;
        _name = name;
    }

    public static ProgramBuilder Define(string id, string name) => new(id, name);

    public ProgramBuilder On(string trigger)
    {
        _trigger = trigger;
        return this;
    }

    public ProgramBuilder Describe(string description)
    {
        _description = description;
        return this;
    }

    public ProgramBuilder Node(string id, string kind, JsonElement config, string inputType = "Json", string outputType = "Json")
    {
        _nodes.Add(new(id, kind, config.Clone(), inputType, outputType));
        return this;
    }

    public ProgramBuilder Node(string id, string kind, string config = "{}", string inputType = "Json", string outputType = "Json")
    {
        using var document = JsonDocument.Parse(config);
        return Node(id, kind, document.RootElement, inputType, outputType);
    }

    public ProgramBuilder Connect(string from, string to)
    {
        _synapses.Add(new(from, to));
        return this;
    }

    public ProgramDefinition Build()
    {
        var definition = new ProgramDefinition(_id, _name, _trigger, [.. _nodes], [.. _synapses], _description);
        var validation = ProgramValidator.Validate(definition);
        if (!validation.Valid)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors));
        }
        return definition;
    }
}
