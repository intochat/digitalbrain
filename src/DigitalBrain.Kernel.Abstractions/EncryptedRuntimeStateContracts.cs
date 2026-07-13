using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Contracts;
using Orleans;

namespace DigitalBrain.Kernel.Runtime;

public static class RuntimeStateStorageProviders
{
    public const string Conversations = "runtime-conversations";
    public const string SurfaceFeeds = "runtime-surface-feeds";
    public const string Sessions = "runtime-sessions";
}

public static class RuntimeStateSchemas
{
    public const int Envelope = 1;
    public const int Conversation = 1;
    public const int ConversationArchive = 1;
    public const int SurfaceFeed = 1;
    public const int Session = 1;
    public const int SynapseTimeline = 1;
    public const int InoEffectPlan = 1;
}

public static class RuntimeStateKinds
{
    public const string Conversation = "conversation";
    public const string ConversationArchive = "conversation-archive";
    public const string SurfaceFeed = "surface-feed";
    public const string Session = "session";
    public const string SynapseTimeline = "synapse-timeline";
    public const string InoEffectPlan = "ino-effect-plan";
}

public static class RuntimeStateKeys
{
    public static string Conversation(
        BrainOwnerId owner,
        ActorId actor,
        string conversationId) =>
        Hash(RuntimeStateKinds.Conversation,
            owner.Value,
            actor.Value,
            conversationId);

    public static string ConversationArchiveSegment(
        string conversationScopeHash,
        string? previousSegmentId,
        long throughSequence,
        string digest)
    {
        DemandScopeHash(conversationScopeHash);
        if (previousSegmentId is not null) DemandScopeHash(previousSegmentId);
        DemandScopeHash(digest);
        if (throughSequence < 1) throw new ArgumentOutOfRangeException(nameof(throughSequence));
        return Hash(
            RuntimeStateKinds.ConversationArchive,
            conversationScopeHash,
            previousSegmentId ?? "genesis",
            throughSequence.ToString(CultureInfo.InvariantCulture),
            digest);
    }

    public static string SurfaceFeed(
        BrainOwnerId owner,
        ActorId actor) =>
        Hash(RuntimeStateKinds.SurfaceFeed,
            owner.Value,
            actor.Value);

    public static string Session(string opaqueSessionId) =>
        Hash(RuntimeStateKinds.Session, opaqueSessionId);

    public static string SynapseTimeline(string neuronId) =>
        Hash(RuntimeStateKinds.SynapseTimeline, neuronId);

    public static bool IsScopeHash(string? value) =>
        value is { Length: 64 } && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static void DemandScopeHash(string value)
    {
        if (!IsScopeHash(value))
            throw new ArgumentException("Runtime state keys must be lowercase SHA-256 scope hashes.", nameof(value));
    }

    private static string Hash(string kind, params string[] components)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        Write(writer, "digitalbrain-runtime-state-v1");
        Write(writer, kind);
        foreach (var component in components)
        {
            if (string.IsNullOrWhiteSpace(component) || component.Length > 1024)
                throw new ArgumentException("Runtime state identity components must be present and bounded.", nameof(components));
            Write(writer, component);
        }
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void Write(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}

public static class RuntimeStateStorageNames
{
    public const string DefaultNamespace = "main";

    public static string NormalizeNamespace(string? value)
    {
        value = value?.Trim().ToLowerInvariant();
        value = string.IsNullOrWhiteSpace(value) ? DefaultNamespace : value;
        if (value.Length > 48 || !char.IsAsciiLetterOrDigit(value[0]) ||
            value.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            throw new ArgumentException("Runtime storage namespace is invalid.", nameof(value));
        return value;
    }

    public static string Container(string storageNamespace, string kind)
    {
        storageNamespace = NormalizeNamespace(storageNamespace);
        if (string.IsNullOrWhiteSpace(kind) || kind.Length > 32 ||
            !char.IsAsciiLetterOrDigit(kind[0]) ||
            kind.Any(static character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("Runtime storage kind is invalid.", nameof(kind));
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(storageNamespace)))[..16];
        return $"dbrt-{digest}-{kind}";
    }
}

public interface IRuntimeStateKeyRing
{
    int ActiveKekVersion { get; }
    ReadOnlyMemory<byte> SigningKey { get; }
    bool TryGetKek(int version, out ReadOnlyMemory<byte> key);
}

[GenerateSerializer]
[Alias("digitalbrain.runtime.encrypted-state-envelope")]
public sealed class EncryptedRuntimeStateEnvelope
{
    [Id(0)] public int EnvelopeVersion { get; set; }
    [Id(1)] public int KekVersion { get; set; }
    [Id(2)] public int SchemaVersion { get; set; }
    [Id(3)] public long Revision { get; set; }
    [Id(4)] public byte[] WrappedDekNonce { get; set; } = [];
    [Id(5)] public byte[] WrappedDekCiphertext { get; set; } = [];
    [Id(6)] public byte[] WrappedDekTag { get; set; } = [];
    [Id(7)] public byte[] PayloadNonce { get; set; } = [];
    [Id(8)] public byte[] PayloadCiphertext { get; set; } = [];
    [Id(9)] public byte[] PayloadTag { get; set; } = [];
    [Id(10)] public byte[] Signature { get; set; } = [];
}

[GenerateSerializer]
[Alias("digitalbrain.runtime.state-conflict")]
public sealed class RuntimeStateConflictException(long expectedRevision, long actualRevision)
    : InvalidOperationException($"Runtime state revision conflict; expected {expectedRevision}, actual {actualRevision}.")
{
    [Id(0)] public long ExpectedRevision { get; } = expectedRevision;
    [Id(1)] public long ActualRevision { get; } = actualRevision;
}

public sealed class RuntimeStateIntegrityException(string reason)
    : IOException($"Encrypted runtime state failed closed: {reason}.");
