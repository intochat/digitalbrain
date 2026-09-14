using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Interactions;
using DigitalBrain.Coding;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Coding;

// The design's phase 1 exit, scripted: the model drives propose, check, commit, build and test through the
// workspace agent, and the rename lands as a git commit on a coding/<id> branch.
public sealed class CodingChatFacts
{
    [Fact]
    public async Task Rename_X_to_Y_lands_as_a_commit_through_the_chat()
    {
        using var fixture = DiskFixture.Create();
        await fixture.InitGitAsync(TestContext.Current.CancellationToken);
        var dotnet = new FakeProcessRunner();
        dotnet.Enqueue(0, "Build succeeded.\n    0 Warning(s)\n    0 Error(s)");
        dotnet.Enqueue(0, "Test run summary: Passed!\n  total: 2\n  failed: 0\n  succeeded: 2\n  skipped: 0\n");
        await using var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(AIModule), typeof(CodingModule)]),
            ConfigureSilo = silo =>
            {
                silo.Services.AddSingleton<ISolutionLoader>(new AdhocSolutionLoader(fixture.Open));
                silo.Services.AddSingleton<IUntrustedContentScreen, ScriptedContentScreen>();
                silo.Services.AddSingleton(new DotnetRunner(dotnet));
            },
            Configuration = new Dictionary<string, string?> { [CodingModule.SolutionPathKey] = fixture.SolutionPath },
        });
        await brain.SiloServices.GetRequiredService<SolutionWorkspace>().WhenReadyAsync(TestContext.Current.CancellationToken);

        using var model = new ScriptedChatClient();
        model.CallTool("code_find_symbols", JsonSerializer.Serialize(new { query = "Greet" }));
        model.CallTool("code_propose_edit", JsonSerializer.Serialize(new { changeId = "rename-1", kind = "Rename", symbolId = "M:Alpha.Greeter.Greet(System.String)", newName = "Hello" }));
        model.CallTool("code_check", JsonSerializer.Serialize(new { changeId = "rename-1" }));
        model.CallTool("code_commit", JsonSerializer.Serialize(new { changeId = "rename-1", message = "rename Greet to Hello" }));
        model.CallTool("code_build", "{}");
        model.CallTool("code_test", "{}");
        model.Say("Renamed Greet to Hello on branch coding/rename-1; build and tests are green.");

        await using var app = await StartAgentAsync(brain, model);
        using var client = app.GetTestClient();
        var events = await TableAgentFacts.RunAsync(client, "Rename Greeter.Greet to Hello and run the tests");
        var toolCalls = events.Where(item => item.GetProperty("type").GetString() == "TOOL_CALL_START")
            .Select(item => item.GetProperty("toolCallName").GetString())
            .ToArray();
        Assert.Equal(["code_find_symbols", "code_propose_edit", "code_check", "code_commit", "code_build", "code_test"], toolCalls);

        var results = events.Where(item => item.GetProperty("type").GetString() == "TOOL_CALL_RESULT")
            .Select(item => JsonSerializer.Deserialize<JsonElement>(item.GetProperty("content").GetString()!))
            .ToArray();

        Assert.Equal(6, results.Length);
        Assert.Contains(results[0].GetProperty("items").EnumerateArray(), hit => hit.GetProperty("id").GetString() == "M:Alpha.Greeter.Greet(System.String)");
        Assert.Equal("Draft", results[1].GetProperty("status").GetString());
        Assert.Equal("Checked", results[2].GetProperty("status").GetString());
        Assert.Equal("Committed", results[3].GetProperty("status").GetString());
        Assert.Equal("coding/rename-1", results[3].GetProperty("branch").GetString());
        Assert.True(results[4].GetProperty("succeeded").GetBoolean());
        Assert.Equal(2, results[5].GetProperty("passed").GetInt32());
        Assert.Contains(""".Hello("world")""", await File.ReadAllTextAsync(fixture.ProgramPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        var log = await new ProcessRunner().RunAsync("git", ["log", "-1", "--format=%s"], fixture.Root, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal("coding: rename Greet to Hello", log.Output.Trim());
        Assert.Contains(events, item => item.GetProperty("type").GetString() == "TEXT_MESSAGE_CONTENT" && item.GetProperty("delta").GetString()!.Contains("coding/rename-1", StringComparison.Ordinal));
    }

    private static async Task<WebApplication> StartAgentAsync(BrainSimulation brain, IChatClient model)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(brain.SiloServices.GetRequiredService<NativeTools>());
        builder.Services.AddSingleton(new ChatClientBuilder(model).UseFunctionInvocation().Build());
        builder.AddConversationalAgent();
        var app = builder.Build();
        app.MapConversationalAgent();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }
}
