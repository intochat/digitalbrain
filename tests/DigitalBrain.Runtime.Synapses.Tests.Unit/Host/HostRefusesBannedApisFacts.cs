using DigitalBrain.Runtime.Synapses.Tests.Unit.Compile;
using DigitalBrain.Spi;

namespace DigitalBrain.Runtime.Synapses.Tests.Unit.Host;

[Capability("stage-read", 1)]
public interface IStageRead
{
    Task<int> Count(CancellationToken ct);
}

public sealed class HostRefusesBannedApisFacts
{
    private const string Key = "Me.Budget";

    private const string NetLeak = "\npublic static class Leak { public static object Client() => new System.Net.Http.HttpClient(); }";

    private static readonly NoBindings None = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Each is appended to the weekly cap source; the analyzer catches most of them, the host must catch all of them.
    public static TheoryData<string, string> Bypasses => new()
    {
        { "System.Net.Http.HttpClient", NetLeak },
        { "System.Type.GetType", "public static class Leak { public static object? Make() => Activator.CreateInstance(Type.GetType(\"System.Net.Http.HttpClient, System.Net.Http\")!); }" },
        { "native import", "public static class Leak { [System.Runtime.InteropServices.DllImport(\"kernel32\")] public static extern int GetTickCount(); }" },
        { "System.Runtime.InteropServices.Marshal", "public static class Leak { public static IntPtr Grab() => System.Runtime.InteropServices.Marshal.AllocHGlobal(4); }" },
        { "System.Runtime.CompilerServices.DynamicAttribute", "public static class Leak { public static object Bind(dynamic x) => x.Anything; }" },
        { "System.Threading.Tasks.TaskFactory", "public static class Leak { public static Task Spawn() => Task.Factory.StartNew(() => { }); }" },
        { "System.Environment.GetEnvironmentVariable", "public static class Leak { public static string? Path() => Environment.GetEnvironmentVariable(\"PATH\"); }" },
        { "System.Linq.Expressions", "public static class Leak { public static object? Exit() => System.Linq.Expressions.Expression.Lambda(System.Linq.Expressions.Expression.Call(typeof(Environment), \"Exit\", null, System.Linq.Expressions.Expression.Constant(0))).Compile().DynamicInvoke(); }" },
        { "System.Threading.Tasks.Task.", "public static class Leak { public static void Spawn() => new Task(() => { }).Start(); }" },
        { "System.Runtime.CompilerServices.Unsafe.As called from Leak", "public static class Leak { public static long Cast(object o) => System.Runtime.CompilerServices.Unsafe.As<object, long>(ref o); }" },
        { "System.Runtime.InteropServices.MemoryMarshal.CreateSpan called from Leak", "public static class Leak { public static int Poke(ref int x) => System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref x, 64)[63]; }" },
        { "DigitalBrain.ISynapseHost", "public static class Leak { public static object? Host(IServiceProvider services) => services.GetService(typeof(ISynapseHost)); }" },
        { "DigitalBrain.Runtime.Synapses.SynapseHost", "public static class Leak { public static Type Host() => typeof(DigitalBrain.Runtime.Synapses.SynapseHost); }" },
        { "System.AppContext.", "public static class Leak { public static object? Data() => AppContext.GetData(\"x\"); }" },
        { "System.Progress", "public static class Leak { public static object Report() => new Progress<int>(_ => { }); }" },
        { "System.Buffers.ArrayPool.get_Shared", "public static class Leak { public static int[] Rent() => System.Buffers.ArrayPool<int>.Shared.Rent(4); }" },
        { "System.Timers", "public static class Leak { public static object Tick() => new System.Timers.Timer(10); }" },
        { "System.Threading.Monitor", "public static class Leak { public static void Hold() => System.Threading.Monitor.Enter(\"x\"); }" },
        { "explicit layout Overlay", "[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)] public struct Overlay { [System.Runtime.InteropServices.FieldOffset(0)] public string Text; [System.Runtime.InteropServices.FieldOffset(0)] public int[] Numbers; }" },
    };

    [Fact]
    public void TheCompilerReportsDB0045()
    {
        var build = new SynapseCompiler(ReferenceSet.FromDefaultContext()).Compile(Source(SynapseCompilerFacts.Budget + NetLeak));

        Assert.False(build.Success);
        Assert.Contains(build.Diagnostics, diagnostic => diagnostic.Id == "DB0045");
    }

    [Fact]
    public Task HostRefusesAnAssemblyThatReferencesSystemNet() => Refuses("System.Net.Http.HttpClient", NetLeak);

    [Theory]
    [MemberData(nameof(Bypasses))]
    public Task HostRefusesWhatTheAnalyzerWasNotThereToSee(string named, string leak) => Refuses(named, leak);

    [Fact]
    public async Task AUnitUsingTheAllowedSurfaceDeploys()
    {
        await using var host = Host(new Installs());
        var outcome = await host.Deploy(Brain("ok"), Source(SynapseCompilerFacts.Budget + """

            public sealed record Note(string Text);

            public static class Allowed
            {
                public static async Task<long> Measure(CancellationToken ct)
                {
                    await Task.Delay(1, ct);
                    using var buffer = new System.IO.MemoryStream(new byte[4]);
                    var note = new Note(nameof(Measure));
                    return buffer.Length + note.ToString().Length + (note == new Note("x") ? 1 : 0) + Environment.NewLine.Length;
                }
            }
            """), None, Ct);

        Assert.True(outcome.Status == DeployStatus.Deployed, outcome.Reason);
    }

    [Fact]
    public async Task AUnitUsingCollectionExpressionsAndSpansDeploys()
    {
        await using var host = Host(new Installs());
        var outcome = await host.Deploy(Brain("cx"), Source(SynapseCompilerFacts.Budget + """

            [System.Runtime.CompilerServices.InlineArray(4)]
            public struct Four { private int first; }

            public sealed record Tag
            {
                public required string Name { get; init; }
            }

            public static class Shapes
            {
                public static int Sum(in Four four)
                {
                    ReadOnlySpan<int> span = [1, 2, 3];
                    List<int> list = [.. span, 4];
                    int[] array = [5, 6];
                    IReadOnlyList<string> names = ["a", "b"];
                    var copy = four;
                    copy[0] = 7;
                    var tag = new Tag { Name = $"t{list.Count}" };
                    var size = tag switch { { Name.Length: > 1 } => 2, _ => 1 };
                    var total = 0;
                    foreach (var item in span) { total += item; }
                    foreach (var item in copy) { total += item; }
                    return total + list.Count + array.Length + names.Count + size + tag.GetHashCode() % 1;
                }
            }
            """), None, Ct);

        Assert.True(outcome.Status == DeployStatus.Deployed, outcome.Reason);
    }

    [Fact]
    public async Task AUnitUsingWhatTheCompilerEmitsDeploys()
    {
        await using var host = Host(new Installs());
        var outcome = await host.Deploy(Brain("ce"), Source(SynapseCompilerFacts.Budget + """

            public sealed record Item
            {
                public required string Name { get; init; }
            }

            public static class Emitted
            {
                private static readonly System.Threading.Lock Gate = new();

                public static async Task<int> Later(CancellationToken ct)
                {
                    await Task.Delay(1, ct);
                    return 1;
                }

                public static IEnumerable<int> Count(int n)
                {
                    for (var i = 0; i < n; i++) { yield return i; }
                }

                public static async IAsyncEnumerable<int> Stream([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
                {
                    await Task.Delay(1, ct);
                    yield return 1;
                }

                public static int Sum(params ReadOnlySpan<int> values)
                {
                    var total = 0;
                    foreach (var value in values) { total += value; }
                    return total;
                }

                public static string Name(int n) => n switch { 1 => "one", 2 => "two" };

                public static int Read(ref readonly int value) => value;

                public static string Locked(Item item)
                {
                    lock (Gate)
                    {
                        var pool = System.Buffers.ArrayPool<int>.Create();
                        return $"{item.Name}:{Sum(1, 2, 3)}:{pool.GetHashCode() % 1}";
                    }
                }
            }
            """), None, Ct);

        Assert.True(outcome.Status == DeployStatus.Deployed, outcome.Reason);
    }

    [Fact]
    public Task AnUnlistedNamespaceIsRefused() =>
        Refuses("System.Xml.Linq.", "public static class Leak { public static string Shape() => new System.Xml.Linq.XElement(\"a\").ToString(); }");

    [Fact]
    public async Task ARefusedDeployKeepsTheStagedBuild()
    {
        var installs = new Installs();
        await using var host = Host(installs);
        var brain = Brain("st");
        Assert.Equal(DeployStatus.Deployed, (await host.Deploy(brain, Source(SynapseCompilerFacts.Budget), None, Ct)).Status);
        installs.Ask = true;
        var withSlot = SynapseCompilerFacts.Budget.Replace(
            "WeeklyCap(Charging s, WeekSpend t)",
            "WeeklyCap(Charging s, WeekSpend t, [Slot] DigitalBrain.Runtime.Synapses.Tests.Unit.Host.IStageRead Db)",
            StringComparison.Ordinal);
        var staged = await host.Deploy(brain, Source(withSlot), None, Ct);
        Assert.True(staged.Status == DeployStatus.Staged, $"{staged.Status} {staged.Reason} {string.Join(",", staged.Installs.Values)}");

        var refused = await host.Deploy(brain, Source(withSlot + NetLeak), None, Ct);

        Assert.Equal(DeployStatus.Refused, refused.Status);
        Assert.Equal(staged.Hash, Assert.Single(await host.List(brain, Ct)).StagedHash);
    }

    private static async Task Refuses(string named, string leak)
    {
        await using var host = Host(new Installs());
        var source = Source(SynapseCompilerFacts.Budget + "\n" + leak);
        Assert.True(host.Compiler.Compile(source).Success);

        var outcome = await host.Deploy(Brain("ba"), source, None, Ct);

        Assert.Equal(DeployStatus.Refused, outcome.Status);
        Assert.Contains(named, outcome.Reason, StringComparison.Ordinal);
        var diagnostic = Assert.Single(outcome.Diagnostics, d => d.Id == "DB0045");
        Assert.Equal(Key, diagnostic.File);
        Assert.Empty(await host.List(Brain("ba"), Ct));
    }

    private static BrainId Brain(string name) => NeuronId.Parse($"{name}/x/y").Brain;

    private static SynapseSource Source(string text) => new(Key, new Dictionary<string, string> { ["Budget.cs"] = text }, null);

    // The analyzer is left out, so only the host's own check stands in front of the load.
    private static SynapseHost Host(Installs installs)
    {
        var catalog = new SynapseCatalogSource(SynapseCatalog.Scan());
        return new(
            new SynapseCompiler(ReferenceSet.FromDefaultContext(), [], withoutBannedApis: true),
            new SynapseLoader(),
            new Registrar(catalog),
            catalog,
            _ => installs,
            new Store(),
            new Listeners());
    }

    private sealed class NoBindings : Dictionary<string, IReadOnlyDictionary<string, string>>;

    // Merges what it activates, as the runtime registrar does, so the host sees the live needs.
    private sealed class Registrar(SynapseCatalogSource catalog) : ISynapseRegistrar
    {
        public Task Activate(BrainId brain, LoadedSource source, CancellationToken ct)
        {
            catalog.Merge(source.Catalog);
            return Task.CompletedTask;
        }

        public Task Deactivate(BrainId brain, string sourceKey, IReadOnlyList<string> aliases, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Installs : IInstalls
    {
        // Once set, every install waits for the person, so a needs-changing update is staged.
        public bool Ask { get; set; }

        public Task<InstallOutcome> Install(InstallRequest request, CancellationToken ct) =>
            Task.FromResult(Ask ? new InstallOutcome(InstallStatus.Pending, "", "plan-1") : new InstallOutcome(InstallStatus.Updated, "", null));

        public Task<string?> Validate(InstallRequest request, CancellationToken ct) => Task.FromResult<string?>(null);

        public Task Retire(string alias, CancellationToken ct) => Task.CompletedTask;

        public Task CancelPending(string alias, CancellationToken ct) => Task.CompletedTask;

        public Task<InstallRecord?> Describe(string alias, CancellationToken ct) =>
            Task.FromResult<InstallRecord?>(new InstallRecord(alias, "", new Dictionary<string, string>(), [], DateTimeOffset.UnixEpoch));

        public Task<IReadOnlyList<InstallRecord>> List(CancellationToken ct) => Task.FromResult<IReadOnlyList<InstallRecord>>([]);

        public Task<IReadOnlyList<PendingInstall>> Pending(CancellationToken ct) => Task.FromResult<IReadOnlyList<PendingInstall>>([]);
    }

    private sealed class Store : ISourceStore
    {
        public List<SourceRecord> Puts { get; } = [];

        public Task<SourceRecord?> Get(BrainId brain, string sourceKey, CancellationToken ct) => Task.FromResult(Puts.LastOrDefault(r => r.SourceKey == sourceKey));

        public Task Put(BrainId brain, SourceRecord record, CancellationToken ct)
        {
            Puts.Add(record);
            return Task.CompletedTask;
        }

        public Task Stage(BrainId brain, string sourceKey, string hash, CancellationToken ct) => Task.CompletedTask;

        public Task Remove(BrainId brain, string sourceKey, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<SourceRecord>> List(BrainId brain, CancellationToken ct) => Task.FromResult<IReadOnlyList<SourceRecord>>([.. Puts]);
    }

    private sealed class Listeners : IClusterListeners
    {
        public Task<IAsyncDisposable> Listen<TSignal>(string listener, Func<NeuronId, TSignal, CancellationToken, Task> handler)
            where TSignal : Signal => Task.FromResult<IAsyncDisposable>(new Nop());

        private sealed class Nop : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
