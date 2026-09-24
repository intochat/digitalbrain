using DigitalBrain.Apps;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

internal static class PackageSamples
{
    public static PackageContent Researcher(string verb) => new(
        new PackageManifest(
            "Internet researcher",
            "Answers a question with a short research brief.",
            [new PackageOperation("research", "Research a question.")],
            [new PackageSetting("style", "How the brief is written.", "plain")]),
        $$"""
        public static class Brief
        {
            public static string Of(string question, string style) => "{{verb}} (" + style + "): " + question;
        }
        """,
        $$"""
        public sealed class BriefFacts
        {
            [Xunit.Fact] public void Writes() => Xunit.Assert.StartsWith("{{verb}}", Brief.Of("q", "plain"));
        }
        """,
        []);
}
