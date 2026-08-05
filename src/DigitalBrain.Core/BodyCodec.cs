using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace DigitalBrain;

internal sealed class BodyCodec
{
    private const string KindProperty = "kind";
    private const string BodyProperty = "body";

    private static readonly HashSet<Type> Terminals =
    [
        typeof(string), typeof(bool), typeof(char), typeof(byte), typeof(sbyte),
        typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long),
        typeof(ulong), typeof(float), typeof(double), typeof(decimal),
        typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan),
        typeof(DateOnly), typeof(TimeOnly), typeof(Guid), typeof(Uri), typeof(JsonElement),
    ];

    private readonly Catalog catalog;

    internal BodyCodec(Catalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        this.catalog = catalog;
        Options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            Converters = { new SynapseConverterFactory(catalog) },
        };
    }

    internal JsonSerializerOptions Options { get; }

    internal JsonElement Encode(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return JsonSerializer.SerializeToElement(value, value.GetType(), Options);
    }

    internal object? Decode(JsonElement element, Type type)
        => JsonSerializer.Deserialize(element, type, Options);

    internal Synapse? DecodeFact(string kind, JsonElement body)
        => catalog.TryGetFactType(kind, out var factType)
            ? (Synapse?)JsonSerializer.Deserialize(body, factType, Options)
            : null; // journals outlive code: unloaded kind → null, never throws

    internal void ValidateVocabulary(Catalog vocabulary)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);

        var visited = new HashSet<Type>();
        foreach (var factType in vocabulary.FactTypes)
        {
            ProbeShape(factType, visited);
        }
    }

    internal void ValidateState(Type stateType)
    {
        ArgumentNullException.ThrowIfNull(stateType);

        if (!stateType.GetConstructors().Any(ctor => ctor.GetParameters().All(p => p.HasDefaultValue)))
        {
            throw new InvalidOperationException(
                $"{Catalog.Describe(stateType)} has no constructor callable without arguments; "
                + "Core must be able to materialize an empty state.");
        }

        foreach (var member in stateType.GetMembers(BindingFlags.Public | BindingFlags.Instance))
        {
            if (member.GetCustomAttribute<System.Runtime.CompilerServices.RequiredMemberAttribute>() is not null)
            {
                throw new InvalidOperationException(
                    $"{Catalog.Describe(stateType)}.{member.Name} is required; a required member bricks "
                    + "materialization of the empty state at activation — give it a default instead.");
            }
        }

        ProbeShape(stateType, []);
    }

    private void ProbeShape(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type))
        {
            return;
        }

        EnsureResolvable(type, type, member: null);
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            ProbeMember(type, property.Name, property.PropertyType, visited);
        }
    }

    private void ProbeMember(Type owner, string member, Type memberType, HashSet<Type> visited)
    {
        memberType = Nullable.GetUnderlyingType(memberType) ?? memberType;
        if (memberType.IsEnum || Terminals.Contains(memberType))
        {
            return;
        }

        if (memberType == typeof(object)
            || typeof(MemberInfo).IsAssignableFrom(memberType)
            || typeof(Delegate).IsAssignableFrom(memberType)
            || typeof(Stream).IsAssignableFrom(memberType)
            || memberType == typeof(nint) || memberType == typeof(nuint))
        {
            throw new InvalidOperationException(
                $"{Catalog.Describe(owner)}.{member} is {Catalog.Describe(memberType)}, which cannot travel "
                + "through the body codec; journals hold data, never runtime artifacts.");
        }

        if (typeof(Synapse).IsAssignableFrom(memberType))
        {
            if (!memberType.IsAbstract)
            {
                ProbeShape(memberType, visited);
            }

            return;
        }

        if (memberType.IsArray)
        {
            ProbeMember(owner, member, memberType.GetElementType()!, visited);
            return;
        }

        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(memberType))
        {
            if (!memberType.IsGenericType)
            {
                throw new InvalidOperationException(
                    $"{Catalog.Describe(owner)}.{member} is the untyped collection {Catalog.Describe(memberType)}; "
                    + "the codec cannot know what to rehydrate — use a generic collection.");
            }

            foreach (var elementType in memberType.GetGenericArguments())
            {
                ProbeMember(owner, member, elementType, visited);
            }

            return;
        }

        if (memberType.IsInterface || memberType.IsAbstract)
        {
            var derivedTypes = memberType.GetCustomAttributes<JsonDerivedTypeAttribute>()
                .Select(attribute => attribute.DerivedType)
                .ToArray();
            if (derivedTypes.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{Catalog.Describe(owner)}.{member} is abstract {Catalog.Describe(memberType)} with no "
                    + "[JsonDerivedType] map; it would serialize its declared shape and never rehydrate.");
            }

            foreach (var derivedType in derivedTypes)
            {
                ProbeShape(derivedType, visited);
            }

            return;
        }

        EnsureResolvable(owner, memberType, member);
        ProbeShape(memberType, visited);
    }

    private void EnsureResolvable(Type owner, Type type, string? member)
    {
        try
        {
            _ = Options.GetTypeInfo(type);
        }
        catch (NotSupportedException failure)
        {
            throw Unresolvable(owner, type, member, failure);
        }
        catch (InvalidOperationException failure)
        {
            throw Unresolvable(owner, type, member, failure);
        }
    }

    private static InvalidOperationException Unresolvable(Type owner, Type type, string? member, Exception failure)
        => new(member is null
            ? $"{Catalog.Describe(owner)} cannot travel through the body codec: {failure.Message}"
            : $"{Catalog.Describe(owner)}.{member} ({Catalog.Describe(type)}) cannot travel through the body codec: {failure.Message}",
            failure);

    private sealed class SynapseConverterFactory(Catalog catalog) : JsonConverterFactory
    {
        // Abstract only — concrete positions serialize bare; keeps factory recursion-free.
        public override bool CanConvert(Type typeToConvert)
            => typeToConvert.IsAbstract && typeof(Synapse).IsAssignableFrom(typeToConvert);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            => (JsonConverter)Activator.CreateInstance(
                typeof(SynapseConverter<>).MakeGenericType(typeToConvert), catalog)!;
    }

    private sealed class SynapseConverter<TSynapse>(Catalog catalog) : JsonConverter<TSynapse>
        where TSynapse : Synapse
    {
        public override TSynapse? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var envelope = JsonDocument.ParseValue(ref reader);
            var root = envelope.RootElement;
            if (!root.TryGetProperty(KindProperty, out var kindProperty)
                || kindProperty.GetString() is not { } kind)
            {
                throw new JsonException(
                    $"A polymorphic synapse position rehydrates from '{KindProperty}'/'{BodyProperty}', "
                    + $"but no '{KindProperty}' is present.");
            }

            if (!catalog.TryGetFactType(kind, out var factType))
            {
                throw new JsonException($"Synapse kind '{kind}' is not in the running catalog.");
            }

            if (!root.TryGetProperty(BodyProperty, out var body))
            {
                throw new JsonException($"Synapse kind '{kind}' carries no '{BodyProperty}'.");
            }

            return JsonSerializer.Deserialize(body, factType, options) as TSynapse
                ?? throw new JsonException(
                    $"Synapse kind '{kind}' rehydrated as {Catalog.Describe(factType)}, "
                    + $"which is not a {typeToConvert.Name}.");
        }

        public override void Write(Utf8JsonWriter writer, TSynapse value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString(KindProperty, catalog.KindOfFact(value.GetType()));
            writer.WritePropertyName(BodyProperty);
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
            writer.WriteEndObject();
        }
    }
}
