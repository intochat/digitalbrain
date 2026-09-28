using System.Text.Json.Nodes;
using IntoChat.Workspace;
using DigitalBrain.Testing.Unit;

namespace IntoChat.Tests.Unit;

public sealed class ShellPersistenceFacts
{
    private const string EmptySnapshot = """{"version":1,"settings":{},"projects":[]}""";

    [Fact]
    public async Task RevisionConflictsAndImportRetriesDoNotReplaceNewerState()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        var state = brain.Get<IShellState>(ShellPersistenceEndpoints.Scope("account", "alice"));
        var other = brain.Get<IShellState>(ShellPersistenceEndpoints.Scope("account", "bob"));
        Assert.Null((await state.Read()).Json);
        var operation = Guid.NewGuid().ToString();
        var imported = await state.Save(0, operation, EmptySnapshot, true);
        Assert.Equal(1, imported.Revision);
        var changed = EmptySnapshot.Replace("\"settings\":{}", "\"settings\":{\"theme\":\"dark\"}", StringComparison.Ordinal);
        var saved = await state.Save(1, Guid.NewGuid().ToString(), changed, false);
        Assert.Equal(2, saved.Revision);
        Assert.Equal(saved, await state.Save(0, operation, EmptySnapshot, true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => state.Save(1, Guid.NewGuid().ToString(), EmptySnapshot, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => state.Save(2, operation, changed, true));
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
        Assert.True(parts.Count >= 5);
        var restored = await ShellSnapshotParts.Read(root, key => Task.FromResult(parts[key]));
        Assert.True(JsonNode.DeepEquals(snapshot, restored));
    }

    [Fact]
    public void ImportPreservesNewerServerRecordsAndAddsMissingProjects()
    {
        var server = JsonNode.Parse("""{"version":1,"settings":{"theme":"dark"},"projects":[{"id":"a","name":"new","conversations":[],"artifacts":[]}]}""")!;
        var legacy = JsonNode.Parse("""{"version":1,"settings":{"theme":"light"},"projects":[{"id":"a","name":"old","conversations":[],"artifacts":[]},{"id":"b","conversations":[],"artifacts":[]}]}""")!;
        Assert.Throws<InvalidOperationException>(() => ShellSnapshotParts.MergeImport(server, legacy));
        legacy["projects"]!.AsArray().RemoveAt(0);
        var merged = ShellSnapshotParts.MergeImport(server, legacy);
        Assert.Equal(2, merged["projects"]!.AsArray().Count);
        Assert.Equal("dark", merged["settings"]!["theme"]!.GetValue<string>());
    }
}
