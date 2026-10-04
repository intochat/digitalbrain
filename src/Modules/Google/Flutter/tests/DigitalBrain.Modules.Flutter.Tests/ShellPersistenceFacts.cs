using System.Text.Json.Nodes;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Testing.Module;

namespace DigitalBrain.Modules.Flutter.Tests;

public sealed class ShellPersistenceFacts
{
    private const string EmptySnapshot = """{"version":1,"settings":{},"projects":[]}""";

    [Fact]
    public async Task EstablishedWorkspaceUsesBoundedPagesInsteadOfThousandsOfSerialCalls()
    {
        var snapshot = JsonNode.Parse(EmptySnapshot)!;
        snapshot["messages"] = new JsonArray(Enumerable.Range(0, 1000).Select(i => (JsonNode?)new JsonObject
        {
            ["id"] = i,
            ["text"] = new string('x', 900),
            ["metadata"] = new JsonObject { ["model"] = "assistant", ["usage"] = new JsonObject { ["tokens"] = i } }
        }).ToArray());
        var parts = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        var active = 0;
        var peak = 0;
        var root = await ShellSnapshotParts.Write(snapshot, async (key, value) =>
        {
            var count = Interlocked.Increment(ref active);
            UpdateMaximum(ref peak, count);
            await Task.Yield();
            parts[key] = value;
            Interlocked.Decrement(ref active);
        });
        Assert.True(parts.Count < 128, $"A 1 MB workspace required {parts.Count} storage writes.");
        Assert.InRange(peak, 2, 16);
        var reads = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        var restored = await ShellSnapshotParts.Read(root, key =>
        {
            reads.AddOrUpdate(key, 1, (_, count) => count + 1);
            return Task.FromResult(parts[key]);
        });
        Assert.True(JsonNode.DeepEquals(snapshot, restored));
        Assert.All(reads.Values, count => Assert.Equal(1, count));
    }

    private static void UpdateMaximum(ref int target, int value)
    {
        int previous;
        do { previous = Volatile.Read(ref target); if (previous >= value) { return; } }
        while (Interlocked.CompareExchange(ref target, value, previous) != previous);
    }

    [Fact]
    public async Task RevisionConflictsAndOperationRetriesDoNotReplaceNewerState()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().StartAsync(ct);
        var state = brain.Get<IShellState>(ShellPersistenceEndpoints.Scope("account", "alice"));
        var other = brain.Get<IShellState>(ShellPersistenceEndpoints.Scope("account", "bob"));
        Assert.Null((await state.Read()).Json);
        var operation = Guid.NewGuid().ToString();
        var first = await state.Save(0, operation, EmptySnapshot);
        Assert.Equal(1, first.Revision);
        var changed = EmptySnapshot.Replace("\"settings\":{}", "\"settings\":{\"theme\":\"dark\"}", StringComparison.Ordinal);
        var savedOperation = Guid.NewGuid().ToString();
        var saved = await state.Save(1, savedOperation, changed);
        Assert.Equal(2, saved.Revision);
        Assert.Equal(saved, await state.Save(1, savedOperation, changed));
        await Assert.ThrowsAsync<InvalidOperationException>(() => state.Save(1, Guid.NewGuid().ToString(), EmptySnapshot));
        await Assert.ThrowsAsync<InvalidOperationException>(() => state.Save(2, savedOperation, EmptySnapshot));
        Assert.Equal(saved, await state.Read());
        Assert.Null((await other.Read()).Json);
    }

    [Fact]
    public async Task LargeArtifactAndTranscriptAreBoundedAndCorruptionIsDetected()
    {
        var snapshot = JsonNode.Parse(EmptySnapshot)!;
        snapshot["content"] = new string('x', 300000);
        snapshot["messages"] = new JsonArray(Enumerable.Range(0, 500).Select(i => (JsonNode?)new JsonObject { ["text"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture) }).ToArray());
        var parts = new Dictionary<string, string>();
        var root = await ShellSnapshotParts.Write(snapshot, (key, value) => { parts[key] = value; return Task.CompletedTask; });
        Assert.All(parts.Values, part => Assert.True(part.Length < 100000));
        Assert.True(JsonNode.DeepEquals(snapshot, await ShellSnapshotParts.Read(root, key => Task.FromResult(parts[key]))));
        parts[root] = "{}";
        await Assert.ThrowsAsync<InvalidDataException>(() => ShellSnapshotParts.Read(root, key => Task.FromResult(parts[key])));
    }
    [Fact]
    public async Task PartitionedSnapshotRoundTripsWithoutPuttingTranscriptInRoot()
    {
        var snapshot = JsonNode.Parse("""{"version":1,"settings":{"theme":"dark"},"selectedProjectId":"a","projects":[{"id":"a","presentation":{},"artifacts":[],"conversations":[{"id":"c","draft":"keep me","messages":[{"text":"hello"}]}]}]}""")!;
        var parts = new Dictionary<string, string>();
        var root = await ShellSnapshotParts.Write(snapshot, (key, value) => { parts[key] = value; return Task.CompletedTask; });
        Assert.DoesNotContain("hello", parts[root]);
        Assert.InRange(parts.Count, 2, 4);
        var restored = await ShellSnapshotParts.Read(root, key => Task.FromResult(parts[key]));
        Assert.True(JsonNode.DeepEquals(snapshot, restored));
    }

    [Fact]
    public async Task PreviousSnapshotFormatReadsSharedPartsOnlyOnce()
    {
        var parts = new Dictionary<string, string>();
        string Add(string json)
        {
            var id = ShellSnapshotParts.Digest(json);
            parts[id] = json;
            return id;
        }
        var emptyObject = Add("""{"objectPages":[]}""");
        var record = Add(new JsonObject { ["object"] = new JsonObject { ["metadata"] = emptyObject } }.ToJsonString());
        var fragment = Add(new JsonObject { ["objectPages"] = new JsonArray(record) }.ToJsonString());
        var page = Add(new JsonObject { ["page"] = new JsonArray(Enumerable.Repeat(fragment, 64).Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()) }.ToJsonString());
        var root = Add(new JsonObject { ["array"] = new JsonArray(page, page) }.ToJsonString());
        var calls = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        var restored = await ShellSnapshotParts.Read(root, async key =>
        {
            calls.AddOrUpdate(key, 1, (_, count) => count + 1);
            await Task.Yield();
            return parts[key];
        });
        Assert.Equal(128, restored.AsArray().Count);
        Assert.All(restored.AsArray(), item => Assert.Empty(item!["metadata"]!.AsObject()));
        Assert.Equal(parts.Count, calls.Count);
        Assert.All(calls.Values, count => Assert.Equal(1, count));
    }
}
