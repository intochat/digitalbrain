using DigitalBrain.Apps;

namespace DigitalBrain.Modules.Apps.Tests.E2E;

// A real package: the behavior answers each invocation of its app, drains invocations it missed while
// starting, and reads its style from the installer's settings.
internal static class ResearcherPackage
{
    public static PackageContent Content(string verb) => new(
        new PackageManifest(
            "Internet researcher",
            "Answers a question with a short research brief.",
            [new PackageOperation("research", "Research a question.")],
            [new PackageSetting("style", "How the brief is written.", "plain")]),
        Source(verb),
        Tests(verb),
        []);

    public static string Tests(string verb) => $$"""
        public sealed class BriefFacts
        {
            [Xunit.Fact]
            public void AnswersInTheChosenStyle()
                => Xunit.Assert.Equal("{{verb}} (plain): q", Brief.Answer(System.Guid.Empty, "q", "plain").Output);
        }
        """;

    private static string Source(string verb) => $$"""
        using DigitalBrain.Apps;
        using DigitalBrain.Apps.Signals;
        using DigitalBrain.Contracts;
        using DigitalBrain.Core;
        using Microsoft.Extensions.Configuration;

        var settings = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args).Build();
        await BehaviorApp.RunAsync<Researcher>(args, brain => [SubscriptionRequirement.For<AppInvoked>(brain.Get<IApp>(settings["Behavior:App"]!))]);

        public sealed class Researcher(IDigitalBrain brain, IConfiguration configuration) : IBehavior
        {
            public async Task RunAsync(CancellationToken cancellation = default)
            {
                var app = brain.Get<IApp>(configuration["Behavior:App"]!);
                var style = configuration["Behavior:Settings:style"] ?? "plain";
                await using var invocations = await brain.SubscribeAsync<AppInvoked>(app, cancellation);
                foreach (var missed in await app.Pending()) { await app.Respond(Brief.Answer(missed.Id, missed.Input, style)); }
                await foreach (var invoked in invocations.ReadAllAsync(cancellation)) { await app.Respond(Brief.Answer(invoked.InvocationId, invoked.Input, style)); }
            }
        }

        public static class Brief
        {
            public static AppResponse Answer(Guid invocation, string question, string style) => new(invocation, "{{verb}} (" + style + "): " + question, null);
        }
        """;
}
