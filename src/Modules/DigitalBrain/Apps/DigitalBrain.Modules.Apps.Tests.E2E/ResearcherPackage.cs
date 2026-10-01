using DigitalBrain.Apps;

namespace DigitalBrain.Modules.Apps.Tests.E2E;

// A real package: the script answers each invocation of its app, drains invocations it missed while
// building, and reads its style from the installer's settings.
internal static class ResearcherPackage
{
    public static PackageContent Content(string verb) => new(
        new PackageManifest(
            "Internet researcher",
            "Answers a question with a short research brief.",
            [new PackageOperation("research", "Research a question.")],
            [new PackageSetting("style", "How the brief is written.", "plain")]),
        Source(verb));

    private static string Source(string verb) => $$"""
        #:project /brain/src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Contracts/DigitalBrain.Modules.Apps.Contracts.csproj
        using DigitalBrain.Apps;
        using DigitalBrain.Apps.Signals;

        await using var brain = await DigitalBrainClient.ConnectAsync(args);
        var app = brain.Get<IApp>(brain.Setting("App")!);
        var style = brain.Setting("style") ?? "plain";
        await using var invocations = await brain.SubscribeAsync<AppInvoked>(app, brain.Stopping);
        foreach (var missed in await app.Pending()) { await app.Respond(Brief.Answer(missed.Id, missed.Input, style)); }
        await foreach (var invoked in invocations.ReadAllAsync(brain.Stopping)) { await app.Respond(Brief.Answer(invoked.InvocationId, invoked.Input, style)); }

        public static class Brief
        {
            public static AppResponse Answer(Guid invocation, string question, string style) => new(invocation, "{{verb}} (" + style + "): " + question, null);
        }
        """;
}
