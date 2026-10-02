#:project /brain/src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Contracts/DigitalBrain.Modules.Apps.Contracts.csproj
#:project /brain/src/Modules/AI/DigitalBrain.Modules.AI.Contracts/DigitalBrain.Modules.AI.Contracts.csproj
using DigitalBrain.AI.Scripted;
using DigitalBrain.Apps;

await using var brain = await DigitalBrainClient.ConnectAsync(args);
var package = PackageId.Parse(brain.Setting("Package")!);
var revision = brain.Setting("Revision")!;
var failures = 0;

await Scenario("The assistant answers with its system prompt", async (app, scope) =>
{
    var helper = brain.Get<IScriptedLLM>(scope + "/helper");
    await helper.Script(["Paris is the capital of France."]);
    await app.Configure(new ConfigureApp(Guid.NewGuid(), new Dictionary<string, string> { ["Model"] = "scripted/" + scope + "/helper" }));

    var answer = await Ask(app, "ask", "What is the capital of France?");

    if (answer != "Paris is the capital of France.") { throw new InvalidOperationException($"The answer was \"{answer}\"."); }
    var prompts = await helper.Prompts();
    if (!prompts.Any(prompt => prompt.Contains("at most three clear sentences", StringComparison.OrdinalIgnoreCase)))
    { throw new InvalidOperationException("The model was never told to answer in at most three clear sentences."); }
});

return failures == 0 ? 0 : 1;

async Task Scenario(string name, Func<IApp, string, Task> run)
{
    var scope = $"specs/{package}@{revision}/{Guid.NewGuid():N}";
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

async Task<string> Ask(IApp app, string operation, string input)
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
    return invocation.Output ?? "";
}
