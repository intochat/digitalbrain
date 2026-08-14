using System.Text.Json.Serialization;
using Brain.Abstractions.Graph;
using Brain.Abstractions.Journal;
using Brain.Abstractions.Runtime;

namespace Brain.Core.Journaling;

[JsonSerializable(typeof(BrainActivitySnapshot))]
[JsonSerializable(typeof(BrainJournalRecord))]
[JsonSerializable(typeof(BrainNeuronView))]
[JsonSerializable(typeof(BrainSynapseView))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(ulong))]
public sealed partial class CoreJournalJsonContext : JsonSerializerContext;
