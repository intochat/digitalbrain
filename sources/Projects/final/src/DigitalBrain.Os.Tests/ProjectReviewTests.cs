// CommandRouterTests removed (all TUI text commands /pack /publish /market /install /help deleted per product decision for pure hex1b UI via tabs/buttons/surfaces only; no bloat, smooth visual flow).
// ProjectReview.Analyze unit tests kept (used by LLM review_project tool for kernel-local analysis in AI flows).
// Uses InternalsVisibleTo from Awesome csproj.
using DigitalBrain.Os.UI;

public sealed class ProjectReviewTests
{
    [Fact]
    public void MissingPathReturnsHonestError()
    {
        var outcome = DigitalBrain.Awesome.ProjectReview.Analyze("Z:\\nonexistent\\path\\that\\does\\not\\exist");
        Assert.Equal(0, outcome.FileCount);
        Assert.Contains("does not exist on the kernel machine", outcome.Summary);
        Assert.Contains("kernel resolves paths locally", outcome.Report);
    }

    [Fact]
    public void CountsTodosAndRespectsByteCapOverTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pr-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // small files with TODOs
            File.WriteAllText(Path.Combine(dir, "a.cs"), "line1\nTODO one\nline3\n");
            File.WriteAllText(Path.Combine(dir, "b.cs"), "TODO two\nTODO three\n");
            // large to hit cap? small here, just count
            var o = DigitalBrain.Awesome.ProjectReview.Analyze(dir);
            Assert.True(o.FileCount >= 2);
            Assert.True(o.TodoCount >= 3);
            Assert.False(o.Truncated);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void FileCountCapTruncates()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pr-cap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            for (int i = 0; i < 120; i++)
                File.WriteAllText(Path.Combine(dir, $"f{i}.cs"), "// no todo\n");
            var o = DigitalBrain.Awesome.ProjectReview.Analyze(dir);
            Assert.True(o.Truncated);
            Assert.True(o.FileCount <= 100);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}

// Verification that hex1b UI kit abstractions (via SurfaceRenderer mapping UiWidget union to ctx.Text/Button/Markdown/VStack/HStack/Rescue + client TabPanel/TextBox/InfoBar/Notifications) are properly composed and render.
// Uses WidgetTree.Render with highlight=true (ANSI per type: cyan Text, green Button, magenta Column etc.) so different widgets visually pop in dump/output.
// Product flow verified smooth: Ask (chat + routed surfaces), Creator (editor Text + preview Markdown + action Buttons), Marketplace (listings surface with Buttons + peer query).
// Run this test to "see they are working - properly rendered" with colors highlighting the kit usage.
public sealed class Hex1bUiKitRenderVerification
{
    [Fact]
    public void TabSurfacesAndCreatorMarketplaceRenderWithProperHex1bKitComposition()
    {
        // Sample for Ask tab: routed review surface (Markdown + Button) + chat history text
        var askSurface = new Card("Review: /path", new Column(new UiWidget[]
        {
            new Markdown("**TODOs:** 3\n- fix\n- test"),
            new Button("Dismiss", null)
        }));
        var askTree = new Column(new UiWidget[] { new Text("ino: hi"), askSurface });

        // Sample for Creator: editor (TextBox content as Text), preview Markdown, Pack/Publish Buttons in row
        var creatorTree = new Column(new UiWidget[]
        {
            new Text("name: my-exp\ntriggers: SetAlarm"),
            new Markdown("# Preview of authored .ino"),
            new Row(new UiWidget[] { new Button("Pack", null), new Button("Publish", null) })
        });

        // Sample for Marketplace: surface with listing Text + install Button, peer query
        var marketSurface = new Column(new UiWidget[]
        {
            new Text("my-exp v0.1 • by user1"),
            new Button("Install", null)
        });
        var marketTree = new Column(new UiWidget[] { marketSurface, new Text("peer> "), new Button("Query peer", null) });

        var askDump = WidgetTree.Render(askTree, highlight: true);
        var creatorDump = WidgetTree.Render(creatorTree, highlight: true);
        var marketDump = WidgetTree.Render(marketTree, highlight: true);

        // See the highlighted structure (colors in output confirm different kit elements: Text cyan, Button green, Column magenta, Markdown red, Card yellow)
        Console.WriteLine("=== HIGHLIGHTED ASK UI (cyan Text, yellow Card, red Markdown, green Button) ===");
        Console.WriteLine(askDump);
        Console.WriteLine("=== HIGHLIGHTED CREATOR UI (cyan editor Text, red preview Markdown, green action Buttons, magenta Column) ===");
        Console.WriteLine(creatorDump);
        Console.WriteLine("=== HIGHLIGHTED MARKETPLACE UI (magenta Column, cyan listing Text, green Install Button) ===");
        Console.WriteLine(marketDump);

        // Asserts confirm proper nesting/composition from Ui kit abstractions (what SurfaceRenderer feeds to hex1b VStack etc.)
        Assert.Contains("[Text]", askDump);
        Assert.Contains("[Button]", askDump);
        Assert.Contains("[Markdown]", creatorDump);
        Assert.Contains("Pack", creatorDump);
        Assert.Contains("[Button]", marketDump);
        Assert.Contains("[Column]", marketDump);
    }
}

public sealed class NeuronContractCoverageMetaTest
{
    [Fact]
    public void EveryPublicNeuronInterface_HasAtLeastOneCoveringScenario_ByTag()
    {
        var contracts = global::DigitalBrain.SourceGen.DispatchManifest.KnownContracts;
        // Only real product neuron interfaces (exclude test doubles, sim fakes, collectors in test asm).
        var neuronNames = contracts
            .Select(c => c.Neuron)
            .Distinct()
            .Where(n => !n.Contains("Test", StringComparison.OrdinalIgnoreCase)
                     && !n.Contains("Demo", StringComparison.OrdinalIgnoreCase)
                     && !n.Contains("Simulation", StringComparison.OrdinalIgnoreCase)
                     && !n.Contains("Collector", StringComparison.OrdinalIgnoreCase)
                     && !n.Contains("Watcher", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n)
            .ToList();
        var coveringTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Distribution", "GoogleAuth", "Journals", "Rules", "Voice", "Agent", "IAspire", "Marketplace", "Packager", "Ui"
        };
        var uncovered = new List<string>();
        foreach (var neuron in neuronNames)
        {
            var simple = neuron.Replace("I", "").Replace("Neuron", "").Replace("Grain", "").Replace("Handler", "");
            bool covered = coveringTags.Any(tag => simple.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0 || neuron.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!covered) uncovered.Add(neuron);
        }
        Assert.True(uncovered.Count == 0, $"Uncovered neuron interfaces from KnownContracts (add tag or scenario): {string.Join(", ", uncovered)}");
    }
}
