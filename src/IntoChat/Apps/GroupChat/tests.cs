#:project /brain/src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Contracts/DigitalBrain.Modules.Apps.Contracts.csproj
#:project /brain/src/Modules/AI/DigitalBrain.Modules.AI.Contracts/DigitalBrain.Modules.AI.Contracts.csproj
using DigitalBrain.AI.GroupChat;
using DigitalBrain.AI.Scripted;
using DigitalBrain.Apps;

await using var brain = await DigitalBrainClient.ConnectAsync(args);
var package = PackageId.Parse(brain.Setting("Package")!);
var revision = brain.Setting("Revision")!;
var failures = 0;

await Scenario("The models take turns and stop once they agree", async (app, scope) =>
{
    await brain.Get<IScriptedLLM>(scope + "/luna").Script([
        "A smart dog bowl.",
        "AGREE: the bowl with a feeding log.",
        "A smart dog bowl that logs every meal.",
    ]);
    await brain.Get<IScriptedLLM>(scope + "/gemma").Script([
        "Add a feeding log to the bowl.",
        "AGREE: bowl plus log.",
    ]);
    await app.Configure(new ConfigureApp(Guid.NewGuid(), new Dictionary<string, string>
    {
        ["LunaModel"] = "scripted/" + scope + "/luna",
        ["GemmaModel"] = "scripted/" + scope + "/gemma",
    }));

    var (answer, invocationId) = await Ask(app, "ask", "Name one product idea for dog owners");

    if (answer != "A smart dog bowl that logs every meal.") { throw new InvalidOperationException($"The answer was \"{answer}\"."); }
    var discussion = await brain.Get<IGroupChat>($"{scope}/app/chat/{invocationId:N}").Read();
    var speakers = discussion.Turns.Select(turn => turn.Speaker).ToArray();
    var expected = speakers.Select((_, index) => index % 2 == 0 ? "Luna" : "Gemma");
    if (speakers.Length < 2 || !speakers.SequenceEqual(expected)) { throw new InvalidOperationException($"The turns went {string.Join(", ", speakers)}."); }
    var told = await brain.Get<IScriptedLLM>(scope + "/gemma").Prompts();
    if (!told.Any(prompt => prompt.Contains("Luna: A smart dog bowl.", StringComparison.Ordinal)))
    { throw new InvalidOperationException("Gemma was never told what Luna said."); }
    var rounds = discussion.Turns.Length == 0 ? 0 : discussion.Turns.Max(turn => turn.Round);
    if (rounds != 2) { throw new InvalidOperationException($"The discussion took {rounds} rounds."); }
    if (!discussion.Agreed) { throw new InvalidOperationException("The participants did not agree."); }
});

await Scenario("Without agreement the discussion stops at the round limit", async (app, scope) =>
{
    await brain.Get<IScriptedLLM>(scope + "/luna").Script(["A leash.", "A collar.", "A leash with a collar."]);
    await brain.Get<IScriptedLLM>(scope + "/gemma").Script(["Not a leash.", "Not a collar."]);
    await app.Configure(new ConfigureApp(Guid.NewGuid(), new Dictionary<string, string>
    {
        ["LunaModel"] = "scripted/" + scope + "/luna",
        ["GemmaModel"] = "scripted/" + scope + "/gemma",
        ["MaxRounds"] = "2",
    }));

    var (answer, invocationId) = await Ask(app, "ask", "Pick one accessory");

    if (answer != "A leash with a collar.") { throw new InvalidOperationException($"The answer was \"{answer}\"."); }
    var discussion = await brain.Get<IGroupChat>($"{scope}/app/chat/{invocationId:N}").Read();
    var rounds = discussion.Turns.Length == 0 ? 0 : discussion.Turns.Max(turn => turn.Round);
    if (rounds != 2) { throw new InvalidOperationException($"The discussion took {rounds} rounds."); }
    if (discussion.Agreed) { throw new InvalidOperationException("The participants agreed."); }
});

return failures == 0 ? 0 : 1;

async Task Scenario(string name, Func<IApp, string, Task> run)
{
    // Scratch installs live inside the host-supplied brain scope, so the scenario's neurons
    // are brain-owned for target authorization instead of failing closed as unclassified.
    var brainScope = brain.Setting("BrainScope");
    var scope = (brainScope is null ? "" : brainScope + "/") + $"specs/{package}@{revision}/{Guid.NewGuid():N}";
    var app = brain.Get<IApp>(scope + "/app");
    try
    {
        await app.Install(new InstallApp(Guid.NewGuid(), new(package, revision), new Dictionary<string, string>()));
        await run(app, scope);
        Console.WriteLine($"dbtest:pass {name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.WriteLine($"dbtest:fail {name}\t{error.Message.ReplaceLineEndings(" ")}");
    }
    finally
    {
        try { await app.Uninstall(new UninstallApp(Guid.NewGuid())); } catch (Exception) { }
    }
}

async Task<(string Answer, Guid InvocationId)> Ask(IApp app, string operation, string input)
{
    var invocation = await app.Invoke(new InvokeApp(Guid.NewGuid(), operation, input));
    var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
    while (invocation.Status == InvocationStatus.Pending)
    {
        if (DateTimeOffset.UtcNow > deadline) { throw new TimeoutException("The app did not answer within 5 minutes."); }
        await Task.Delay(200);
        invocation = await app.ReadInvocation(invocation.Id);
    }
    if (invocation.Status == InvocationStatus.Failed) { throw new InvalidOperationException(invocation.Error ?? "The app failed."); }
    return (invocation.Output ?? "", invocation.Id);
}
