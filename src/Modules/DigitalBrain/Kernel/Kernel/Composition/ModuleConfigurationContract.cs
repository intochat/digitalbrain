using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DigitalBrain.Core;

/// <summary>Copies option values and transports only explicitly declared public members.</summary>
public abstract class ModuleConfigurationContract<TModule, TOptions>(params string[] members)
    : IModuleConfigurationContract where TModule : class, IModule, new() where TOptions : class, new()
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private readonly HashSet<string> _members = new(members, StringComparer.Ordinal);
    public Type ModuleType => typeof(TModule);
    public Type OptionsType => typeof(TOptions);
    public object CreateDefaults() => new TOptions();
    public object Copy(object options) => Decode(Encode(options));
    public ModuleDefinition Compile(object options) => Compile(Require(options));
    protected abstract ModuleDefinition Compile(TOptions options);
    protected virtual void Validate(TOptions options) { }
    public string WriteReplacement(object configured) => WriteOverride(configured, _members);

    public string WriteOverride(object configured, IReadOnlyCollection<string> assignedMembers)
    {
        var source = Encode(configured);
        var patch = new JsonObject();
        foreach (var member in assignedMembers)
        {
            CheckMember(member);
            patch[member] = Read(source, member)?.DeepClone();
        }
        return patch.ToJsonString();
    }

    public object ApplyOverride(object baseline, string json)
    {
        var result = Encode(baseline);
        JsonObject patch;
        try { patch = JsonNode.Parse(json) as JsonObject ?? throw new JsonException(); }
        catch (JsonException) { throw new ArgumentException("Invalid module override.", nameof(json)); }
        foreach (var pair in patch)
        {
            CheckMember(pair.Key);
            Write(result, pair.Key, pair.Value?.DeepClone());
        }
        try { return Decode(result); }
        catch (JsonException) { throw new ArgumentException("Invalid module option value.", nameof(json)); }
    }

    private void CheckMember(string member)
    {
        if (!_members.Contains(member)) { throw new ArgumentException($"Unknown public option '{member}' for {typeof(TModule).Name}."); }
    }

    private TOptions Require(object options)
    {
        var typed = options as TOptions
            ?? throw new ArgumentException($"Options must be {typeof(TOptions).Name}.", nameof(options));
        Validate(typed);
        return typed;
    }
    private JsonObject Encode(object options) => JsonSerializer.SerializeToNode(Require(options), Json)!.AsObject();
    private static TOptions Decode(JsonObject options) => options.Deserialize<TOptions>(Json)!;
    private static JsonNode? Read(JsonObject source, string path)
    {
        JsonNode? value = source;
        foreach (var part in path.Split('.')) { value = value?[part]; }
        return value;
    }
    private static void Write(JsonObject target, string path, JsonNode? value)
    {
        var parts = path.Split('.');
        for (var index = 0; index < parts.Length - 1; index++)
        {
            target[parts[index]] ??= new JsonObject();
            target = target[parts[index]]!.AsObject();
        }
        target[parts[^1]] = value;
    }
}