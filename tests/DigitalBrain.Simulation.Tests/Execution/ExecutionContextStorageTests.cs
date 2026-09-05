using DigitalBrain.Execution;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace DigitalBrain.Simulation.Tests.Execution;

public sealed class ExecutionContextStorageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pre_module_context_snapshot_reads_all_nested_values_without_changing_payloads(bool arraySlots)
    {
        const string snapshot = """
            {
              "$type": "DigitalBrain.Abstractions.Execution.ExecutionContextState, DigitalBrain.Abstractions",
              "ExecutionId": {
                "$type": "DigitalBrain.Abstractions.Execution.ExecutionId, DigitalBrain.Abstractions",
                "Value": "c6cb97aa-c994-47f0-84b4-c47172731dcd"
              },
              "Slots": {
                "$type": "System.Collections.Generic.List`1[[DigitalBrain.Abstractions.Execution.ContextSlot, DigitalBrain.Abstractions]], System.Private.CoreLib",
                "$values": [{
                  "$type": "DigitalBrain.Abstractions.Execution.ContextSlot, DigitalBrain.Abstractions",
                  "Path": { "$type": "DigitalBrain.Abstractions.Execution.ContextPath, DigitalBrain.Abstractions", "Value": "chat.turn.old" },
                  "Entry": {
                    "$type": "DigitalBrain.Abstractions.Execution.ContextEntry, DigitalBrain.Abstractions",
                    "SchemaHash": "chat.turn.v1",
                    "PayloadJson": "DigitalBrain.Abstractions.Execution.ExecutionId, DigitalBrain.Abstractions",
                    "BlobRef": null,
                    "Digest": { "$type": "DigitalBrain.Abstractions.Execution.ContextDigest, DigitalBrain.Abstractions", "Sha256Hex": "old-digest" }
                  }
                }]
              }
            }
            """;

        var stored = arraySlots ? snapshot.Replace(
            "System.Collections.Generic.List`1[[DigitalBrain.Abstractions.Execution.ContextSlot, DigitalBrain.Abstractions]], System.Private.CoreLib",
            "DigitalBrain.Abstractions.Execution.ContextSlot[], DigitalBrain.Abstractions", StringComparison.Ordinal) : snapshot;
        var state = JsonConvert.DeserializeObject<ExecutionContextState>(stored, Settings());

        Assert.NotNull(state);
        Assert.Equal(ExecutionId.Parse("c6cb97aac99447f084b4c47172731dcd"), state.ExecutionId);
        var slot = Assert.Single(state.Slots);
        Assert.Equal("chat.turn.old", slot.Path.Value);
        Assert.Equal("chat.turn.v1", slot.Entry.SchemaHash);
        Assert.Equal("old-digest", slot.Entry.Digest.Sha256Hex);
        Assert.Equal("DigitalBrain.Abstractions.Execution.ExecutionId, DigitalBrain.Abstractions", slot.Entry.PayloadJson);

        var current = JsonConvert.SerializeObject(state, Settings());
        Assert.Contains("DigitalBrain.Execution.ExecutionId, DigitalBrain.Modules.Execution.Contracts", current, StringComparison.Ordinal);
        Assert.Equal(state.ExecutionId, JsonConvert.DeserializeObject<ExecutionContextState>(current, Settings())!.ExecutionId);
    }

    [Fact]
    public void Unknown_legacy_types_are_not_remapped()
    {
        var binder = new ExecutionContextSerializationBinder(new DefaultSerializationBinder());
        Assert.Throws<JsonSerializationException>(() => binder.BindToType(
            "DigitalBrain.Abstractions", "DigitalBrain.Abstractions.Execution.UnknownType"));
    }

    private static JsonSerializerSettings Settings() => new()
    {
        TypeNameHandling = TypeNameHandling.All,
        SerializationBinder = new ExecutionContextSerializationBinder(new DefaultSerializationBinder()),
    };
}
