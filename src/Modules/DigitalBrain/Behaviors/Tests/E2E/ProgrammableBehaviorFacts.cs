using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.Behavior;
using DigitalBrain.Coding;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Text;
using DigitalBrain.Time;
using IntoChat;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Tests;

public sealed class ProgrammableBehaviorFacts
{
    [Fact]
    public async Task AgentAuthoredCSharpRunsUpdatesStopsAndRollsBackInASeparateWorker()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(8));
        var ct = deadline.Token;
        var root = Path.Combine(Path.GetTempPath(), "brain-behavior-e2e", Guid.NewGuid().ToString("N"));
        using var model = new ScriptedModel();
        var ai = new AIOptions { Default = new() { Profile = "fixture" } };
        ai.ModelProfiles.Add("fixture", new() { Provider = "OpenAI", Model = "fixture", Endpoint = model.Url, Capabilities = LlmCapabilities.None });
        try
        {
            DigitalBrain.Testing.E2E.E2ETestBuilder CreateHost() => E2ETest.Create().WithModule<AIModule>(m => m.WithOptions(ai))
                .WithModule<CodingModule>().WithModule<BehaviorModule>().WithModule<TimeModule>()
                .WithModule<FlutterModule>(flutter => flutter.BackendOnly())
                .WithModule<BehaviorFixtureModule>().WithExecution(new TestExecutionOptions
                {
                    PrivateConfiguration = new Dictionary<string, string?>
                    {
                        ["DigitalBrain:AI:OpenAI:ApiKey"] = "test-only",
                        [CodeExecutionOptions.SectionName + ":Root"] = Path.Combine(root, "coding"),
                        [BehaviorOptions.SectionName + ":Root"] = Path.Combine(root, "behaviors"),
                        [BehaviorOptions.SectionName + ":StartupTimeout"] = "00:00:05",
                        [BehaviorOptions.SectionName + ":HeartbeatInterval"] = "00:00:00.250",
                    },
                });
            await using var brain = await StartHost(CreateHost(), ct);
            using var http = new HttpClient { BaseAddress = brain.HttpClient.BaseAddress, Timeout = TimeSpan.FromMinutes(5) };
            var answer = model.Reply(Source, Tests, ct);
            using var authored = await http.PostAsJsonAsync("/behavior-fixture/author", new { draftId = "timer", intent = "Display formatted timer ticks", activate = true }, ct);
            var body = await authored.Content.ReadAsStringAsync(ct);
            Assert.True(authored.IsSuccessStatusCode, body);
            var result = JsonSerializer.Deserialize<AuthorBehaviorResult>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.True(result.Artifact is not null, result.Error);
            await answer;
            var program = brain.Get<IBehaviorProgram>(BehaviorToolScope.Key("workspace-fixture", "timer"));
            var state = await Ready(program, ct);
            Assert.Equal(result.Artifact.Id, state.Deployments.Single().Artifact.Id);
            var text = brain.Get<IText>("stage2-output");
            var timer = brain.Get<DigitalBrain.Time.Timers.ITimer>("stage2-timer");
            await timer.Start(TimeSpan.Zero);
            await Output(text, "tick stage2-timer", ct);
            var logs = await program.ReadLogs(0, 100, ct);
            var hostPid = await http.GetFromJsonAsync<int>("/behavior-fixture/pid", ct);
            Assert.Contains(logs.Entries, e => e.Message.Contains("WORKERPID:", StringComparison.Ordinal) && !e.Message.Contains("WORKERPID:" + hostPid, StringComparison.Ordinal));

            var updatedAnswer = model.Reply(Source.Replace("\"tick ", "\"updated ", StringComparison.Ordinal), Tests.Replace("\"tick ", "\"updated ", StringComparison.Ordinal), ct);
            using var updated = await http.PostAsJsonAsync("/behavior-fixture/author", new { draftId = "timer", intent = "Change the timer prefix to updated", activate = true }, ct);
            var updateBody = await updated.Content.ReadAsStringAsync(ct);
            Assert.True(updated.IsSuccessStatusCode, updateBody);
            await updatedAnswer;
            state = await Ready(program, ct);
            Assert.Equal(2, state.DesiredDeploymentRevision);
            await timer.Start(TimeSpan.Zero);
            await Output(text, "updated stage2-timer", ct);
            await program.Stop(new(state.Revision, Guid.NewGuid()), ct);
            state = await State(program, BehaviorExecutionState.Stopped, ct);
            await program.Rollback(new(state.Revision, Guid.NewGuid(), 1), ct);
            state = await Ready(program, ct);
            Assert.Equal(result.Artifact.Id, state.Deployments.Last().Artifact.Id);
            await timer.Start(TimeSpan.Zero);
            await Output(text, "tick stage2-timer", ct);
            await program.Stop(new(state.Revision, Guid.NewGuid()), ct);
            await State(program, BehaviorExecutionState.Stopped, ct);
            await timer.Start(TimeSpan.Zero);
            await Task.Delay(250, ct);
            Assert.Equal("tick stage2-timer", (await text.Read()).Markdown);

            var completedDraft = brain.Get<ICodeDraft>("completed-example");
            await completedDraft.Save(new(0, Guid.NewGuid(), """
                using DigitalBrain.Core;
                await BehaviorApp.RunAsync<Example>(args, _ => []);
                public sealed class Example : IBehavior
                {
                    public Task RunAsync(CancellationToken cancellation = default) => Task.CompletedTask;
                }
                """, "public sealed class UserTests { [Xunit.Fact] public async Task Completes() { await new Example().RunAsync(); } }", []), ct);
            var checkedCompletion = await Check(completedDraft, 1, ct);
            Assert.True(checkedCompletion.Artifact is not null, string.Join("\r\n", checkedCompletion.Diagnostics.Select(x => x.Message)));
            var completedProgram = brain.Get<IBehaviorProgram>("completed-example");
            await completedProgram.Deploy(new(0, Guid.NewGuid(), checkedCompletion.Artifact!, "{}"), ct);
            var completed = await State(completedProgram, BehaviorExecutionState.Completed, ct);
            await Task.Delay(1500, ct);
            Assert.Equal(completed.GenerationId, (await completedProgram.Read(ct)).GenerationId);
            Assert.Equal(BehaviorExecutionState.Completed, (await completedProgram.Read(ct)).State);

            await completedDraft.Save(new(1, Guid.NewGuid(), "this is invalid C#", "tests", []), ct);
            var failed = await Check(completedDraft, 2, ct);
            Assert.Equal(CodeCheckStatus.Failed, failed.Status);
            Assert.Null(failed.Artifact);

            var unavailableSource = Source.Replace(
                "await using var ticks = await brain.SubscribeAsync<TimerTick>(brain.Get<Timer>(\"stage2-timer\"), cancellation);",
                "await Task.Delay(Timeout.Infinite, cancellation); await using var ticks = await brain.SubscribeAsync<TimerTick>(brain.Get<Timer>(\"stage2-timer\"), cancellation);", StringComparison.Ordinal);
            await completedDraft.Save(new(2, Guid.NewGuid(), unavailableSource, Tests, ["time", "flutter"]), ct);
            var unavailable = await Check(completedDraft, 3, ct);
            Assert.True(unavailable.Artifact is not null, string.Join("\r\n", unavailable.Diagnostics.Select(x => x.Message)));
            var unhealthy = brain.Get<IBehaviorProgram>("unready-example");
            await unhealthy.Deploy(new(0, Guid.NewGuid(), unavailable.Artifact!, "{}"), ct);
            var unready = await State(unhealthy, BehaviorExecutionState.Failed, ct);
            Assert.False(unready.Ready);
            Assert.Contains("startup deadline", unready.Error, StringComparison.Ordinal);
            await unhealthy.Stop(new(unready.Revision, Guid.NewGuid()), ct);
            await State(unhealthy, BehaviorExecutionState.Stopped, ct);

            var crashSource = """
                using DigitalBrain.Core;
                await BehaviorApp.RunAsync<Example>(args, _ => []);
                public sealed class Example : IBehavior
                {
                    public async Task RunAsync(CancellationToken cancellation = default)
                    { await Task.Delay(500, cancellation); throw new InvalidOperationException("intentional crash"); }
                }
                """;
            await completedDraft.Save(new(3, Guid.NewGuid(), crashSource,
                "public sealed class UserTests { [Xunit.Fact] public async Task ReportsFailure() { await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => new Example().RunAsync()); } }", []), ct);
            var crash = await Check(completedDraft, 4, ct);
            Assert.True(crash.Artifact is not null, string.Join("\r\n", crash.Diagnostics.Select(x => x.Message)));
            var crashing = brain.Get<IBehaviorProgram>("crashing-example");
            await crashing.Deploy(new(0, Guid.NewGuid(), crash.Artifact!, "{}"), ct);
            var crashed = await State(crashing, BehaviorExecutionState.Failed, ct);
            Assert.False(crashed.Ready);
            Assert.Contains((await crashing.ReadLogs(0, 500, ct)).Entries, e => e.Message.Contains("intentional crash", StringComparison.Ordinal));
            await crashing.Stop(new(crashed.Revision, Guid.NewGuid()), ct);
            await State(crashing, BehaviorExecutionState.Stopped, ct);

            state = await program.Read(ct);
            await program.Start(new(state.Revision, Guid.NewGuid()), ct);
            var beforeRestart = await Ready(program, ct);
            var latestLogs = await program.ReadLogs(0, 500, ct);
            var pidMessage = latestLogs.Entries.Last(e => e.GenerationId == beforeRestart.GenerationId && e.Message.Contains("WORKERPID:", StringComparison.Ordinal)).Message;
            var workerPid = int.Parse(System.Text.RegularExpressions.Regex.Match(pidMessage, @"WORKERPID:(\d+)").Groups[1].Value);
            using (var workerProcess = System.Diagnostics.Process.GetProcessById(workerPid))
            using (var hostProcess = System.Diagnostics.Process.GetProcessById(hostPid))
            {
                hostProcess.Kill();
                await hostProcess.WaitForExitAsync(ct);
                await workerProcess.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
            }
            await brain.DisposeAsync();
            await using var restarted = await StartHost(CreateHost(), ct);
            var recoveredProgram = restarted.Get<IBehaviorProgram>(BehaviorToolScope.Key("workspace-fixture", "timer"));
            var recovered = await Ready(recoveredProgram, ct);
            Assert.NotEqual(beforeRestart.GenerationId, recovered.GenerationId);
            Assert.Equal(beforeRestart.Deployments.Last().Artifact.Id, recovered.Deployments.Last().Artifact.Id);
            Assert.Equal(BehaviorDesiredState.Stopped, (await restarted.Get<IBehaviorProgram>("crashing-example").Read(ct)).DesiredState);
            await restarted.Get<DigitalBrain.Time.Timers.ITimer>("stage2-timer").Start(TimeSpan.Zero);
            await Output(restarted.Get<IText>("stage2-output"), "tick stage2-timer", ct);
            await recoveredProgram.Stop(new(recovered.Revision, Guid.NewGuid()), ct);
            await State(recoveredProgram, BehaviorExecutionState.Stopped, ct);
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Task<DigitalBrain.Testing.E2E.E2EBrain> StartHost(DigitalBrain.Testing.E2E.E2ETestBuilder builder, CancellationToken ct)
        => builder.StartAsync(ct);

    private static async Task<CodeCheckSnapshot> Check(ICodeDraft draft, long revision, CancellationToken ct)
    {
        var check = await draft.Check(new(revision, Guid.NewGuid()), ct);
        while (check.Status is CodeCheckStatus.Queued or CodeCheckStatus.Building or CodeCheckStatus.Testing)
        { await Task.Delay(100, ct); check = await draft.ReadCheck(check.OperationId, ct); }
        return check;
    }

    private static async Task<BehaviorSnapshot> Ready(IBehaviorProgram program, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        while (true)
        {
            var state = await program.Read(timeout.Token);
            if (state.Ready) { return state; }
            if (state.State == BehaviorExecutionState.Failed)
            {
                var logs = await program.ReadLogs(0, 100, timeout.Token);
                Assert.Fail(state.Error + "\r\n" + string.Join("\r\n", logs.Entries.Select(x => x.Message)));
            }
            await Task.Delay(50, timeout.Token);
        }
    }
    private static async Task<BehaviorSnapshot> State(IBehaviorProgram program, BehaviorExecutionState expected, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        while (true)
        {
            var state = await program.Read(timeout.Token);
            if (state.State == expected) { return state; }
            if (state.State == BehaviorExecutionState.Failed)
            { Assert.Fail(state.Error + "\r\n" + string.Join("\r\n", (await program.ReadLogs(0, 100, timeout.Token)).Entries.Select(x => x.Message))); }
            await Task.Delay(50, timeout.Token);
        }
    }
    private static async Task Output(IText text, string expected, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while ((await text.Read()).Markdown != expected) { await Task.Delay(50, timeout.Token); }
    }

    private const string Source = """
        using DigitalBrain.Contracts;
        using DigitalBrain.Core;
        using DigitalBrain.Time.Timers.Signals;
        using DigitalBrain.Flutter.Text;
        using Timer = DigitalBrain.Time.Timers.ITimer;
        await BehaviorApp.RunAsync<TimerToText>(args, brain => [SubscriptionRequirement.For<TimerTick>(brain.Get<Timer>("stage2-timer"))]);
        public sealed class TimerToText(IDigitalBrain brain) : IBehavior
        {
            public static string Format(TimerTick tick) => "tick " + tick.TimerId;
            public async Task RunAsync(CancellationToken cancellation = default)
            {
                Console.WriteLine("WORKERPID:" + Environment.ProcessId);
                await using var ticks = await brain.SubscribeAsync<TimerTick>(brain.Get<Timer>("stage2-timer"), cancellation);
                await foreach (var tick in ticks.ReadAllAsync(cancellation))
                { await brain.Get<IText>("stage2-output").Set(Format(tick)); }
            }
        }
        """;
    private const string Tests = """
        public sealed class TimerTests
        {
            [Xunit.Fact]
            public void FormatsTheObservedTimerIdentity()
            {
                var tick = new DigitalBrain.Time.Timers.Signals.TimerTick("sample", DateTimeOffset.UnixEpoch);
                Xunit.Assert.Equal("tick sample", TimerToText.Format(tick));
            }
        }
        """;

    private sealed class ScriptedModel : IDisposable
    {
        private readonly HttpListener _listener = new();
        public string Url { get; }
        public ScriptedModel()
        {
            using var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            Url = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
        }
        public async Task Reply(string source, string tests, CancellationToken ct)
        {
            var context = await _listener.GetContextAsync().WaitAsync(ct);
            using var reader = new StreamReader(context.Request.InputStream);
            await reader.ReadToEndAsync(ct);
            var response = JsonSerializer.Serialize(new
            {
                id = Guid.NewGuid().ToString("N"),
                @object = "chat.completion",
                created = 1,
                model = "fixture",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = JsonSerializer.Serialize(new { source, tests, moduleIds = new[] { "time", "flutter" } }) }, finish_reason = "stop" } },
            });
            var bytes = Encoding.UTF8.GetBytes(response);
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(bytes, ct);
            context.Response.Close();
        }
        public void Dispose() { _listener.Close(); }
    }
}

public sealed class BehaviorFixtureModule : IModule
{
    public void Configure(ISiloBuilder silo)
    {
        silo.Services.Configure<BehaviorAuthoringOptions>(o => { o.AllowActivation = true; o.ModelProfile = "fixture"; });
        silo.Services.AddSingleton<BehaviorToolService>();
        silo.Services.AddSingleton<BehaviorAuthoringService>();
    }
    public void Configure(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/behavior-fixture/author", async (AuthorBehaviorRequest request, BehaviorAuthoringService author, CancellationToken ct) =>
        {
            try { return Results.Ok(await author.AuthorAsync("workspace-fixture", request, ct)); }
            catch (Exception error) { return Results.Problem(error.ToString(), statusCode: 400); }
        });
        endpoints.MapGet("/behavior-fixture/pid", () => Environment.ProcessId);
    }
}