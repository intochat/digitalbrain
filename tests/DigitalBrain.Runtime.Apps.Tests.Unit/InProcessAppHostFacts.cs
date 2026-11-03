using DigitalBrain.Apps.Hosting;
using DigitalBrain.Examples.Calculator;
using DigitalBrain.Runtime.Apps;
using DigitalBrain.UI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DigitalBrain.Runtime.Apps.Tests.Unit;

public sealed class InProcessAppHostFacts
{
    [Fact]
    public async Task AssignsAPrebuiltAppAndRunsATurn()
    {
        var sink = new RecordingSink();
        var host = new InProcessAppHost((_, _) => new object(), _ => new object(), sink);
        var bytes = CompileTinyApp("TinyApp", "tiny", "hello from prebuilt");
        var assignment = new ProgramAssignment(
            "b/apps/tiny",
            "h1",
            new Dictionary<string, PrebuiltBehavior> { ["app.dll"] = new(bytes, [], "h1") },
            new Dictionary<string, string>());

        await host.Assign(assignment, CancellationToken.None);
        await host.Request("b/apps/tiny", "open", null, CancellationToken.None);

        Assert.Equal("b/apps/tiny", sink.LastOwner);
        Assert.NotNull(sink.LastMain);
        Assert.Equal("Tiny", sink.LastMain.Title);
        var col = Assert.IsType<Column>(sink.LastMain.Root);
        var text = Assert.IsType<Text>(Assert.Single(col.Children));
        Assert.Equal("hello from prebuilt", text.Value);
    }

    [Fact]
    public async Task ContractAssembliesResolveFromTheHost()
    {
        var sink = new RecordingSink();
        var host = new InProcessAppHost((_, _) => new object(), _ => new object(), sink);
        var bytes = CompileTinyApp("ContractCheckApp", "contract-check", "ok");
        var assignment = new ProgramAssignment(
            "b/apps/contract-check",
            "h2",
            new Dictionary<string, PrebuiltBehavior> { ["app.dll"] = new(bytes, [], "h2") },
            new Dictionary<string, string>());

        await host.Assign(assignment, CancellationToken.None);
        var turns = host.TryGetTurns("b/apps/contract-check");
        Assert.NotNull(turns);

        var loadedAppType = turns.App.GetType();
        Assert.NotSame(typeof(App).Assembly, loadedAppType.Assembly);
        Assert.Same(typeof(App).Assembly, loadedAppType.BaseType!.BaseType!.Assembly);

        var renderMethod = loadedAppType.GetMethod("Render")!;
        Assert.Same(typeof(View).Assembly, renderMethod.ReturnType.Assembly);
    }

    [Fact]
    public void RetiredProgramIsCollected()
    {
        var bytes = CompileTinyApp("CollectibleApp", "collectible", "temp");
        var weak = AssignAndCaptureWeakContext(bytes);

        for (var i = 0; i < 10 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(weak.IsAlive);
    }

    [Fact]
    public async Task AssignTypeHostsACompiledApp()
    {
        var sink = new RecordingSink();
        var host = new InProcessAppHost((_, _) => new object(), _ => new object(), sink);

        await host.AssignType("b/apps/calculator", typeof(CalculatorApp), new Dictionary<string, string>(), CancellationToken.None);
        await host.Request("b/apps/calculator", "press", "7", CancellationToken.None);
        await host.Request("b/apps/calculator", "press", "+", CancellationToken.None);
        await host.Request("b/apps/calculator", "press", "5", CancellationToken.None);
        await host.Request("b/apps/calculator", "press", "=", CancellationToken.None);

        Assert.NotNull(sink.LastMain);
        var col = Assert.IsType<Column>(sink.LastMain.Root);
        var text = Assert.IsType<Text>(Assert.Single(col.Children));
        Assert.Equal("12", text.Value);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference AssignAndCaptureWeakContext(byte[] bytes)
    {
        var sink = new RecordingSink();
        var host = new InProcessAppHost((_, _) => new object(), _ => new object(), sink);
        var assignment = new ProgramAssignment(
            "b/apps/collectible",
            "h3",
            new Dictionary<string, PrebuiltBehavior> { ["app.dll"] = new(bytes, [], "h3") },
            new Dictionary<string, string>());
        host.Assign(assignment, CancellationToken.None).GetAwaiter().GetResult();
        var alc = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(host.TryGetTurns("b/apps/collectible")!.App.GetType().Assembly)!;
        var weak = new WeakReference(alc, trackResurrection: true);
        host.Retire("b/apps/collectible", CancellationToken.None).GetAwaiter().GetResult();
        Assert.Null(host.TryGetTurns("b/apps/collectible"));
        return weak;
    }

    private static byte[] CompileTinyApp(string className, string appId, string text)
    {
        var source = $$"""
            using DigitalBrain;
            using DigitalBrain.UI;

            public sealed record TinyState(string Text = "")
            {
                public TinyState() : this("") { }
            }

            [App("{{appId}}")]
            public sealed class {{className}} : App<TinyState>
            {
                [On]
                public void Open() => Set(new TinyState("{{text}}"));

                public View Render() => new("Tiny", new Column([new Text(State.Text)]));
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p))
            .Cast<MetadataReference>()
            .ToList();

        var compilation = CSharpCompilation.Create(
            $"TestDynamic_{className}_{Guid.NewGuid():N}",
            [syntaxTree],
            trustedAssemblies,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return ms.ToArray();
    }

    private sealed class RecordingSink : IPresentationSink
    {
        public string? LastOwner { get; private set; }
        public View? LastSide { get; private set; }
        public View? LastMain { get; private set; }

        public Task Present(string owner, View? side, View? main, CancellationToken ct)
        {
            LastOwner = owner;
            LastSide = side ?? LastSide;
            LastMain = main ?? LastMain;
            return Task.CompletedTask;
        }
    }
}
