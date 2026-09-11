using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class WorkspaceFacts
{
    [Fact]
    public async Task Documents_survive_reload_and_stale_updates_do_not_overwrite()
    {
        var directory = Path.Combine(Path.GetTempPath(), "workspace-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var app = await StartAsync(directory);
            using var client = app.GetTestClient();
            var created = await client.PostAsJsonAsync("/workspace/artifacts", new { kind = "diagram", title = "CRM", content = new { source = "```sketch\nrect \"Account\" at 0,0 size 180x90\n```" } }, TestContext.Current.CancellationToken);
            created.EnsureSuccessStatusCode();
            var document = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            var id = document.GetProperty("id").GetString();
            var updated = await client.PutAsJsonAsync($"/workspace/artifacts/{id}", new { expectedRevision = 1, title = "CRM revised", content = new { source = "saved edit" } }, TestContext.Current.CancellationToken);
            updated.EnsureSuccessStatusCode();
            var stale = await client.PutAsJsonAsync($"/workspace/artifacts/{id}", new { expectedRevision = 1, title = "stale", content = new { source = "lost edit" } }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            await using var reloaded = await StartAsync(directory);
            using var second = reloaded.GetTestClient();
            var saved = await second.GetFromJsonAsync<JsonElement>($"/workspace/artifacts/{id}", TestContext.Current.CancellationToken);
            Assert.Equal("CRM revised", saved.GetProperty("title").GetString());
            Assert.Equal(2, saved.GetProperty("revision").GetInt32());
            Assert.Equal("saved edit", saved.GetProperty("content").GetProperty("source").GetString());
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); } }
    }

    [Fact]
    public async Task Invalid_graph_links_and_document_paths_are_rejected()
    {
        var directory = Path.Combine(Path.GetTempPath(), "workspace-test-" + Guid.NewGuid().ToString("N"));
        await using var app = await StartAsync(directory);
        using var client = app.GetTestClient();
        var invalid = await client.PostAsJsonAsync("/workspace/artifacts", new
        {
            kind = "brain", title = "Invalid", content = new
            {
                nodes = new[] { new { id = "a", type = "agent", name = "a", label = "Agent" } },
                synapses = new[] { new { id = "edge", sourceId = "a", targetId = "missing", signalType = "asks", kind = "draft" } },
            },
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var badId = await client.GetAsync("/workspace/artifacts/not-an-artifact", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, badId.StatusCode);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task Voice_returns_a_draft_without_starting_a_conversation()
    {
        await using var app = await StartAsync(Path.GetTempPath());
        using var client = app.GetTestClient();
        using var form = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "audio", "voice.wav" } };
        var response = await client.PostAsync("/agent/transcribe", form, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Draft words", body.GetProperty("text").GetString());
        Assert.False(body.TryGetProperty("turnId", out _));
        Assert.False(body.TryGetProperty("runId", out _));
    }

    private static async Task<WebApplication> StartAsync(string directory)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["DigitalBrain:Workspace:StoragePath"] = directory });
        builder.Services.AddSingleton<WorkspaceArtifactStore>();
        builder.Services.AddSingleton<IAudioTranscriptionService, Transcription>();
        var app = builder.Build();
        app.MapWorkspaceEndpoints();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }
    private sealed class Transcription : IAudioTranscriptionService
    {
        public bool IsReady => true;
        public bool InitializationFailed => false;
        public string ModelId => "fixture";
        public string? ErrorMessage => null;
        public Task<string> TranscribeAsync(string audioFilePath, CancellationToken cancellationToken = default) => Task.FromResult("Draft words");
        public Task<string> TranscribeAsync(Stream audio, string fileName, CancellationToken cancellationToken = default) => Task.FromResult("  Draft words  ");
    }
}
