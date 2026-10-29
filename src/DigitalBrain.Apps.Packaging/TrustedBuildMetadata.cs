using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Contracts;

namespace DigitalBrain.Apps.Packaging;

/// <summary>Local build seam. Only call with trusted developer types, never while inspecting a received archive.</summary>
public static class TrustedBuildMetadata
{
    public static CompiledAppDescriptor Extract(Type entryType, AppManifest manifest, CompiledAppDescriptor? hostMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(entryType);
        ArgumentNullException.ThrowIfNull(manifest);
        if (!typeof(App).IsAssignableFrom(entryType) || entryType.GetCustomAttribute<AppAttribute>()?.Id != manifest.Id)
        { throw new InvalidDataException("Typed manifest and entry app identity disagree."); }
        if (hostMetadata is not null && (hostMetadata.Id != manifest.Id || (hostMetadata.Capabilities.Length != 0 && !hostMetadata.Capabilities.Order(StringComparer.Ordinal).SequenceEqual(manifest.Requires.Capabilities.Select(c => c.ToString()).Order(StringComparer.Ordinal), StringComparer.Ordinal))))
        { throw new InvalidDataException("Host metadata cannot change framework identity or capabilities."); }
        var descriptors = new List<CompiledNeuronDescriptor>();
        var contracts = new Dictionary<Type, CompiledContractDescriptor>();
        foreach (var neuron in manifest.Neurons)
        {
            var attribute = neuron.GetCustomAttribute<NeuronAttribute>() ?? throw new InvalidDataException("Owned neuron lacks frozen identity.");
            var stateType = StateType(neuron);
            var state = stateType is null ? null : Contract(stateType);
            if (stateType is not null) { contracts[stateType] = state!; }
            var supplied = hostMetadata?.Neurons.FirstOrDefault(n => n.TypeName == PortableName(neuron));
            var policy = neuron.IsDefined(typeof(StatelessNeuronAttribute), false) ? "stateless" : neuron.IsDefined(typeof(ReentrantAttribute), true) ? "reentrant" : "affine";
            if (supplied is not null && (supplied.NeuronType != attribute.Type || supplied.StateName != (attribute.State ?? "state") || supplied.HostPolicy != policy))
            { throw new InvalidDataException("Host metadata cannot change frozen neuron identity or policy."); }
            descriptors.Add(new(PortableName(neuron), attribute.Type, attribute.State ?? "state", policy, state)
            { LegacyOwnership = supplied?.LegacyOwnership ?? [] });
        }
        foreach (var type in entryType.Assembly.GetTypes().Where(t => t.GetCustomAttribute<NeuronAliasAttribute>() is not null || t.GetCustomAttribute<SignalAttribute>() is not null))
        { contracts.TryAdd(type, Contract(type)); }
        var descriptor = hostMetadata ?? new CompiledAppDescriptor
        {
            Id = manifest.Id,
            Name = manifest.Name,
            Version = manifest.Version,
            EntryAssembly = entryType.Assembly.GetName().Name + ".dll",
            EntryType = PortableName(entryType)
        };
        return PackageFormat.Canonical(descriptor with
        {
            Id = manifest.Id,
            Name = manifest.Name,
            Version = manifest.Version,
            EntryAssembly = entryType.Assembly.GetName().Name + ".dll",
            EntryType = PortableName(entryType),
            Capabilities = [.. manifest.Requires.Capabilities.Select(c => c.ToString())],
            Neurons = [.. descriptors],
            Contracts = [.. contracts.Where(c => !typeof(Signal).IsAssignableFrom(c.Key)).Select(c => c.Value)],
            Signals = [.. contracts.Where(c => typeof(Signal).IsAssignableFrom(c.Key)).Select(c => c.Value)],
            Payload = []
        });
    }

    private static Type? StateType(Type neuron)
    {
        for (var current = neuron; current is not null; current = current.BaseType)
        { if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Neuron<>)) { return current.GetGenericArguments()[0]; } }
        return null;
    }

    private static CompiledContractDescriptor Contract(Type type)
    {
        var alias = type.GetCustomAttribute<NeuronAliasAttribute>()?.Alias ?? type.GetCustomAttribute<SignalAttribute>()?.Alias
            ?? throw new InvalidDataException($"{type.FullName} needs an explicit frozen serializer alias.");
        object? defaults = type.GetConstructor(Type.EmptyTypes) is not null ? Activator.CreateInstance(type) : null;
        var fields = new List<CompiledFieldDescriptor>();
        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance).Where(m => m is PropertyInfo { GetMethod: not null } or FieldInfo))
        {
            if (member.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition == JsonIgnoreCondition.Always) { continue; }
            if (member is PropertyInfo property && property.GetIndexParameters().Length != 0) { continue; }
            var field = member.GetCustomAttribute<FieldAttribute>() ?? throw new InvalidDataException($"{type.FullName}.{member.Name} needs a stable field id.");
            var memberType = member is PropertyInfo p ? p.PropertyType : ((FieldInfo)member).FieldType;
            var name = member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? member.Name;
            var required = member.GetCustomAttribute<RequiredMemberAttribute>() is not null || member.GetCustomAttribute<JsonRequiredAttribute>() is not null
                || type.GetConstructors().Any(c => c.GetParameters().Any(p => p.Name?.Equals(member.Name, StringComparison.OrdinalIgnoreCase) == true && !p.HasDefaultValue));
            var defaultJson = defaults is null ? null : JsonSerializer.Serialize(member is PropertyInfo prop ? prop.GetValue(defaults) : ((FieldInfo)member).GetValue(defaults), memberType);
            fields.Add(new(field.Id, name, PortableName(memberType), required, defaultJson));
        }
        return new(PortableName(type), alias, "1") { Fields = [.. fields.OrderBy(f => f.Id)] };
    }

    private static string PortableName(Type type)
    {
        if (type.IsArray) { return PortableName(type.GetElementType()!) + "[]"; }
        if (!type.IsGenericType) { return type.FullName ?? throw new InvalidDataException("Unnamed metadata type."); }
        return type.GetGenericTypeDefinition().FullName!.Split('`')[0] + "<" + string.Join(",", type.GetGenericArguments().Select(PortableName)) + ">";
    }
}
