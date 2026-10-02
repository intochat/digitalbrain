using System.Reflection;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Architecture.Tests;

internal static class PersistedStateRules
{
    internal static IEnumerable<MemberInfo> Members(Type type) => type
        .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
        .Where(member => member is PropertyInfo or FieldInfo && member.IsDefined(typeof(IdAttribute)));

    internal static Dictionary<string, uint> Ids(Type type) => Members(type)
        .ToDictionary(member => member.Name, member => member.GetCustomAttribute<IdAttribute>()!.Id);

    internal static IEnumerable<string> IdViolations(Type type, uint[] retired)
    {
        var members = Members(type).ToArray();
        var ids = members.Select(member => member.GetCustomAttribute<IdAttribute>()!.Id).ToArray();
        foreach (var duplicate in ids.GroupBy(id => id).Where(group => group.Count() > 1))
        {
            yield return $"{type.FullName}: duplicate Id({duplicate.Key})";
        }
        foreach (var id in retired.Intersect(ids)) { yield return $"{type.FullName}: retired Id({id}) reused"; }
        var occupied = ids.Concat(retired).Order().ToArray();
        if (occupied.Length > 0 && !occupied.SequenceEqual(Enumerable.Range(0, occupied.Length).Select(id => (uint)id)))
        {
            yield return $"{type.FullName}: IDs must be contiguous, including explicitly retired IDs";
        }
    }

    internal static IEnumerable<string> ShapeViolations(Type type, uint[] retired)
    {
        foreach (var violation in IdViolations(type, retired)) { yield return violation; }
        foreach (var member in ContractRules.PublicMembers(type).Where(member => member.DeclaringType == type
            && (member is FieldInfo || member is PropertyInfo { SetMethod: not null })))
        {
            if (!member.IsDefined(typeof(IdAttribute))
                && !member.IsDefined(typeof(System.Runtime.Serialization.IgnoreDataMemberAttribute))
                && !member.IsDefined(typeof(NonSerializedAttribute)))
            {
                yield return $"{type.FullName}.{member.Name}: persisted member has no Id";
            }
        }
        foreach (var member in Members(type).Where(member => ContainsInterfaceCollection(ContractRules.MemberType(member))))
        {
            yield return $"{type.FullName}.{member.Name}: persisted collections must use concrete arrays, not collection interfaces";
        }
    }

    private static bool ContainsInterfaceCollection(Type type) =>
        (type.IsInterface && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
        || (type.HasElementType && ContainsInterfaceCollection(type.GetElementType()!))
        || (type.IsGenericType && type.GetGenericArguments().Any(ContainsInterfaceCollection));

    internal static IEnumerable<string> CompatibilityViolations(Dictionary<string, uint> previous, Dictionary<string, uint> current)
    {
        foreach (var (member, id) in previous)
        {
            if (!current.TryGetValue(member, out var actual) || actual != id)
            {
                yield return $"{member}: preserve Id({id}); retire removed IDs explicitly";
            }
        }
        foreach (var (member, id) in current.Where(entry => !previous.ContainsKey(entry.Key)))
        {
            if (previous.Count > 0 && id <= previous.Values.Max())
            {
                yield return $"{member}: Id({id}) must append after Id({previous.Values.Max()})";
            }
        }
    }

    internal static Type[] Discover(IEnumerable<Type> productionTypes)
    {
        var types = productionTypes.ToArray();
        var assemblies = types.Select(type => type.Assembly).ToHashSet();
        var pending = new Queue<Type>(types.SelectMany(type =>
            type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType)
                .Concat(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(field => field.FieldType)))
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IPersistentState<>))
            .Select(type => type.GetGenericArguments()[0]));
        HashSet<Type> visited = [];
        HashSet<Type> states = [];
        while (pending.TryDequeue(out var type))
        {
            if (!visited.Add(type)) { continue; }
            if (type.HasElementType) { pending.Enqueue(type.GetElementType()!); }
            foreach (var argument in type.GetGenericArguments()) { pending.Enqueue(argument); }
            if (!assemblies.Contains(type.Assembly)) { continue; }
            foreach (var derived in types.Where(candidate => candidate != type && type.IsAssignableFrom(candidate)
                && candidate.IsDefined(typeof(GenerateSerializerAttribute), false)))
            {
                pending.Enqueue(derived);
            }
            if (type.BaseType is { } parent) { pending.Enqueue(parent); }
            if (type.IsEnum || !type.IsDefined(typeof(GenerateSerializerAttribute), false)) { continue; }
            states.Add(type);
            foreach (var member in Members(type)) { pending.Enqueue(ContractRules.MemberType(member)); }
        }
        return states.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
    }
}
