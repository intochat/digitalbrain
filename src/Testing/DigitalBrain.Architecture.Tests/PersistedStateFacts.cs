using System.Text.Json;
using Orleans;

namespace DigitalBrain.Architecture.Tests;

public sealed class PersistedStateFacts
{
    // These IDs were retired before the architecture suite existed. Never fill their gaps.
    private static readonly Dictionary<string, uint[]> RetiredIds = new()
    {
        ["DigitalBrain.Apps.AppState"] = [2],
        ["DigitalBrain.Apps.CommitPackage"] = [3],
        ["DigitalBrain.Apps.PackageRevision"] = [3],
        ["DigitalBrain.Assistant.AssistantState"] = [5],
        ["DigitalBrain.Compute.Storage.ComputeRecordsState"] = [1],
        ["DigitalBrain.Flutter.WebBrowser.Signals.BrowserConnected"] = [1],
        ["DigitalBrain.Flutter.Workspace.ShellHeadState"] = [2],
        ["DigitalBrain.Google.Gmail.GmailState"] = [2, 3],
        ["DigitalBrain.Memory.MemoryNamespace"] = [2],
        ["DigitalBrain.Platform.Secrets.SecretsState"] = [3],
        ["DigitalBrain.Platform.Secrets.SecretRecord"] = [6]
    };

    private static readonly Type[] States = PersistedStateRules.Discover(ProductionArchitecture.Types);
    private static readonly Type[] SerializedTypes = ProductionArchitecture.Types
        .Where(type => !type.IsEnum && type.IsDefined(typeof(GenerateSerializerAttribute), false))
        .OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();

    [Fact]
    public void SerializedIdsAreContiguousIncludingRetiredSlots()
    {
        Assert.NotEmpty(SerializedTypes);
        Assert.All(RetiredIds.Keys, name => Assert.Contains(SerializedTypes, type => type.FullName == name));
        var violations = SerializedTypes.SelectMany(type => PersistedStateRules.IdViolations(type, RetiredIds.GetValueOrDefault(type.FullName!, []))).ToArray();
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void PersistedStateHasStableIdsAndConcreteCollections()
    {
        Assert.NotEmpty(States);
        Assert.Contains(States, type => type.FullName == "DigitalBrain.Core.BrainState");
        Assert.Contains(States, type => type.FullName == "DigitalBrain.Platform.Secrets.SecretsState");
        var violations = States.SelectMany(type => PersistedStateRules.ShapeViolations(type, RetiredIds.GetValueOrDefault(type.FullName!, []))).ToArray();
        var legacy = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "LegacyCollectionMembers.txt"))
            .Where(line => line.Length > 0 && !line.StartsWith('#')).ToArray();
        var collectionViolations = violations.Where(message => message.Contains(": persisted collections", StringComparison.Ordinal)).ToArray();
        var unexpected = violations.Except(legacy.Select(member => member + ": persisted collections must use concrete arrays, not collection interfaces")).ToArray();
        Assert.True(unexpected.Length == 0, string.Join(Environment.NewLine, unexpected));
        var stale = legacy.Where(member => !collectionViolations.Contains(member + ": persisted collections must use concrete arrays, not collection interfaces")).ToArray();
        Assert.True(stale.Length == 0, "Remove resolved collection exceptions: " + string.Join(", ", stale));
    }

    [Fact]
    public void PersistedIdsMatchTheReviewedCompatibilityBaseline()
    {
        var baseline = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, uint>>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PersistedStateIds.json")))!;
        var current = SerializedTypes.ToDictionary(type => type.FullName!, type =>
        {
            var ids = PersistedStateRules.Ids(type);
            foreach (var id in RetiredIds.GetValueOrDefault(type.FullName!, [])) { ids.Add($"<retired:{id}>", id); }
            return ids.OrderBy(entry => entry.Value).ToDictionary(entry => entry.Key, entry => entry.Value);
        });
        var actual = JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true });
        // A candidate is diagnostic output only: normal test runs never rewrite the reviewed source baseline.
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "PersistedStateIds.actual.json"), actual);
        Assert.Empty(baseline.SelectMany(entry => current.TryGetValue(entry.Key, out var members)
            ? PersistedStateRules.CompatibilityViolations(entry.Value, members).Select(message => entry.Key + ": " + message)
            : [$"{entry.Key}: persisted type removed"]));
        Assert.Equal(JsonSerializer.Serialize(baseline), JsonSerializer.Serialize(current));
    }
}
