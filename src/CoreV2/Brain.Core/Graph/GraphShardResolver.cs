using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Policy;
using Brain.Core.Endpoints;
using Brain.Core.Modules;

namespace Brain.Core.Graph;

internal readonly record struct GraphShardId
{
    internal GraphShardId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        Value = value;
    }

    internal string Value { get; }
}

// The physical partition is derived only from the outbound source address. Each
// field is length framed before hashing, so separator characters remain data.
internal sealed class GraphShardResolver
{
    internal GraphShardId Resolve(EndpointAddress source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Workspace.IsEmpty
            || string.IsNullOrWhiteSpace(source.Module.Value)
            || string.IsNullOrWhiteSpace(source.Role.Value)
            || string.IsNullOrWhiteSpace(source.ScopeToken))
        {
            throw new ArgumentException("A graph shard source requires all endpoint dimensions.", nameof(source));
        }

        var material = new ArrayBufferWriter<byte>();
        Append(material, source.Workspace.Value);
        Append(material, source.Module.Value);
        Append(material, source.Role.Value);
        Append(material, source.ScopeToken);
        return new GraphShardId(Convert.ToHexString(SHA256.HashData(material.WrittenSpan)));
    }

    private static void Append(ArrayBufferWriter<byte> writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var length = writer.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        writer.Advance(sizeof(int));
        writer.Write(bytes);
    }
}

// The directory is composition-owned and injected. It is deliberately not a
// static registry: callers that share this directory share the authoritative
// source shard, while separate compositions remain isolated.
internal sealed class GraphShardDirectory(GraphShardResolver resolver)
{
    private readonly GraphShardResolver _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    private readonly ConcurrentDictionary<GraphShardId, GraphShardEntry> _entries = [];

    internal BrainGraphShardGrain Open(
        EndpointAddress source,
        ModuleSet modules,
        IWorkspacePolicyEvaluator policy)
    {
        var id = _resolver.Resolve(source);
        var entry = _entries.GetOrAdd(id, _ => new GraphShardEntry(id, source));
        if (entry.Source != source)
        {
            throw new InvalidOperationException("A graph shard id cannot be shared by distinct source endpoints.");
        }

        return new BrainGraphShardGrain(source, entry, new SynapseRevisionValidator(modules, policy));
    }
}

internal sealed class GraphShardEntry(GraphShardId id, EndpointAddress source)
{
    internal GraphShardId Id { get; } = id;

    internal EndpointAddress Source { get; } = source ?? throw new ArgumentNullException(nameof(source));

    internal BrainGraphShardState State { get; } = new();

    internal object Gate { get; } = new();
}
