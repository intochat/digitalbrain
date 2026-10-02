using System.Text.Json;
using DigitalBrain.Apps;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace DigitalBrain.Modules.Apps.Tests.Unit.Packages;

public sealed class PackageCollectionSerializationFacts
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevisionCollectionsRetainTheirWireShapeAndNulls(bool populated)
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        var content = PackageSamples.Researcher("Research") with
        {
            Files = populated ? new() { ["prompts/main.txt"] = "Find sources" } : null,
        };
        content = content with { Manifest = content.Manifest with
        {
            Accounts = populated ? [new("mail", "gmail", "Read mail")] : null,
        } };
        var revision = new PackageRevision("revision", ["first", "second"], content, "alice", "Merge", DateTimeOffset.UnixEpoch);
        var serializer = brain.Brain.SiloServices.GetRequiredService<Serializer>();
        var restored = serializer.Deserialize<PackageRevision>(serializer.SerializeToArray(revision));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(restored, options);
        Assert.Equal(JsonSerializer.Serialize(revision, options), json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("parents").ValueKind);
        var wireContent = document.RootElement.GetProperty("content");
        Assert.Equal(populated ? JsonValueKind.Object : JsonValueKind.Null, wireContent.GetProperty("files").ValueKind);
        Assert.Equal(populated ? JsonValueKind.Array : JsonValueKind.Null, wireContent.GetProperty("manifest").GetProperty("accounts").ValueKind);
        var fromJson = JsonSerializer.Deserialize<PackageRevision>(json, options)!;
        Assert.Equal(revision.Parents, fromJson.Parents);
        Assert.Equal(revision.Content.Manifest.Operations, fromJson.Content.Manifest.Operations);
        Assert.Equal(revision.Content.Manifest.Settings, fromJson.Content.Manifest.Settings);
        Assert.Equal(revision.Content.Manifest.Accounts, fromJson.Content.Manifest.Accounts);
        Assert.Equal(revision.Content.Files, fromJson.Content.Files);
    }
}
