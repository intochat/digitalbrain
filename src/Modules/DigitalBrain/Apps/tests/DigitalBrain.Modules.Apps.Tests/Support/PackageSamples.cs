using DigitalBrain.Apps;

namespace DigitalBrain.Modules.Apps.Tests;

internal static class PackageSamples
{
    public static PackageContent Tracker() => new(
        new PackageManifest(
            "Tracker",
            "Tracks a feed with two behaviors.",
            [new PackageOperation("ask", "Ask the tracker.")],
            []),
        "",
        new Dictionary<string, string>
        {
            [PackageContent.BehaviorsPrefix + "track.cs"] = "// watches the feed",
            [PackageContent.BehaviorsPrefix + "report.cs"] = "// renders the report",
        });

    public static PackageContent Researcher(string verb) => new(
        new PackageManifest(
            "Internet researcher",
            "Answers a question with a short research brief.",
            [new PackageOperation("research", "Research a question.")],
            [new PackageSetting("style", "How the brief is written.", "plain"), new PackageSetting("language", "Language of the brief.", "en")]),
        $$"""
        public static class Brief
        {
            public static string Of(string question, string style) => "{{verb}} (" + style + "): " + question;
        }
        """);
}
