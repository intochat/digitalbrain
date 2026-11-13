using DigitalBrain.Kernel;
using DigitalBrain.Units;
using DigitalBrain.Runtime.Synapses.Tests.Unit.Compile;

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
        { "DigitalBrain.Units.ISynapseHost", "public static class Leak { public static object? Host(IServiceProvider services) => services.GetService(typeof(DigitalBrain.Units.ISynapseHost)); }" },
        { "DigitalBrain.Runtime.Synapses.UnitDeployer", "public static class Leak { public static Type Host() => typeof(DigitalBrain.Runtime.Synapses.UnitDeployer); }" },
        { "System.AppContext.", "public static class Leak { public static object? Data() => AppContext.GetData(\"x\"); }" },
        { "System.Progress", "public static class Leak { public static object Report() => new Progress<int>(_ => { }); }" },
        { "System.Buffers.ArrayPool.get_Shared", "public static class Leak { public static int[] Rent() => System.Buffers.ArrayPool<int>.Shared.Rent(4); }" },
        { "System.Timers", "public static class Leak { public static object Tick() => new System.Timers.Timer(10); }" },
        { "System.Threading.Monitor", "public static class Leak { public static void Hold() => System.Threading.Monitor.Enter(\"x\"); }" },
        // C1: RuntimeHelpers is allowed member by member; Box forges object references.
        { "System.Runtime.CompilerServices.RuntimeHelpers.Box", "public static class Leak { public static object Forge(byte[] b) => System.Runtime.CompilerServices.RuntimeHelpers.Box(ref b[0], default); }" },
        { "System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject", "public static class Leak { public static object Make() => System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Leak)); }" },
        // I3: System.Type is allowed member by member; Type-taking serializer overloads and typeof in attribute blobs are refused.
        { "System.Type.get_TypeHandle", "public static class Leak { public static RuntimeTypeHandle Handle() => typeof(Leak).TypeHandle; }" },
        { "System.Type.GetInterfaces", "public static class Leak { public static int Walk(object o) => o.GetType().GetInterfaces().Length; }" },
        { "System.Type.MakeGenericType", "public static class Leak { public static Type Make() => typeof(List<>).MakeGenericType(typeof(int)); }" },
        { "System.Text.Json.JsonSerializer.Deserialize(System.String,System.Type", "public static class Leak { public static object? Make(Type t) => System.Text.Json.JsonSerializer.Deserialize(\"{}\", t); }" },
        { "System.Text.Json.JsonSerializer.Serialize(System.Object,System.Type", "public static class Leak { public static string Read(object o, Type t) => System.Text.Json.JsonSerializer.Serialize(o, t); }" },
        { "typeof(System.Net.Http.HttpClient) in an attribute", "public sealed class TagAttribute(Type type) : Attribute { public Type Type { get; } = type; }\n[Tag(typeof(System.Net.Http.HttpClient))] public static class Leak { }" },
        { "typeof(DigitalBrain.Runtime.Synapses.UnitDeployer) in an attribute", "public sealed class TagAttribute : Attribute { public Type[] Types { get; set; } = []; }\n[Tag(Types = [typeof(int), typeof(DigitalBrain.Runtime.Synapses.UnitDeployer)])] public static class Leak { }" },
        // I4: nothing leaves the activation's turn.
        { "System.Threading.Tasks.Task.ConfigureAwait", "public static class Leak { public static async Task Go() => await Task.Delay(1).ConfigureAwait(false); }" },
        { "System.Threading.Tasks.ValueTask.ConfigureAwait", "public static class Leak { public static async Task Go() => await default(ValueTask).ConfigureAwait(false); }" },
        { "System.Threading.Tasks.Task.Yield", "public static class Leak { public static async Task Go() => await Task.Yield(); }" },
        { "System.Threading.CancellationToken.Register", "public static class Leak { public static object Go(CancellationToken ct) => ct.Register(() => { }); }" },
        { "System.Threading.CancellationTokenSource.CancelAfter", "public static class Leak { public static void Go(CancellationTokenSource s) => s.CancelAfter(5); }" },
        { "System.Threading.CancellationTokenSource.Cancel", "public static class Leak { public static void Go(CancellationTokenSource s) => s.Cancel(true); }" },
        { "System.Threading.CancellationTokenSource..ctor", "public static class Leak { public static object Go() => new CancellationTokenSource(5); }" },
        { "System.Runtime.CompilerServices.TaskAwaiter.OnCompleted called from Leak", "public static class Leak { public static void Go() => Task.Delay(1).GetAwaiter().OnCompleted(() => { }); }" },
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
        var host = Host(new Installs());
        var outcome = await host.Deploy(Brain("ok"), Source(SynapseCompilerFacts.Budget + """

            public sealed record Note(string Text);

            public sealed class TagAttribute : Attribute { public Type[] Types { get; set; } = []; }

            [Tag(Types = [typeof(List<string>), typeof(Note), typeof(Signal), typeof(System.Threading.CancellationToken), typeof(DigitalBrain.Runtime.Synapses.Tests.Unit.Host.IStageRead)])]
            public static class Tagged { }

            public static class Streams
            {
                public static async IAsyncEnumerable<int> Numbers()
                {
                    await Task.Delay(1);
                    yield return 1;
                    yield return 2;
                }

                public static async Task<int> Sum()
                {
                    var total = 0;
                    await foreach (var n in Numbers()) { total += n; }
                    return total;
                }
            }

            public static class Allowed
            {
                public static async Task<long> Measure(CancellationToken ct)
                {
                    await Task.Delay(1, ct);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    using var own = new CancellationTokenSource();
                    own.Cancel();
                    _ = typeof(Note).FullName + (typeof(Note) == typeof(Tagged) ? "" : "x");
                    await new ValueTask(Task.CompletedTask);
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
        var host = Host(new Installs());
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
        var host = Host(new Installs());
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
    public async Task NonCodeFilesRideInTheSourceAndItsHashButDoNotCompile()
    {
        var deployment = new FakeDeployment(new Installs());
        var host = Host(new Installs(), deployment);
        var files = new Dictionary<string, string>
        {
            ["Budget.cs"] = SynapseCompilerFacts.Budget,
            ["spec.md"] = "# Weekly cap\nnot C# at all {",
            ["scenarios.json"] = "[{\"publish\": \"payments.charging\"}]",
        };

        var outcome = await host.Deploy(Brain("nc"), new SynapseSource(Key, files, null), None, Ct);

        Assert.True(outcome.Status == DeployStatus.Deployed, outcome.Reason);
        Assert.NotNull(deployment.LastBuild);
        Assert.Equal(["Budget.cs", "scenarios.json", "spec.md"], deployment.LastBuild.Files.Keys.Order(StringComparer.Ordinal));
        var changed = new Dictionary<string, string>(files) { ["spec.md"] = "# Weekly cap, revised" };
        Assert.NotEqual(outcome.Hash, host.Compiler.Compile(new SynapseSource(Key, changed, null)).Hash);
    }

    [Fact]
    public void AnOversizedNonCodeFileFailsTheBuild()
    {
        var files = new Dictionary<string, string> { ["Budget.cs"] = SynapseCompilerFacts.Budget, ["spec.md"] = new string('x', SynapseCompiler.MaxSidecarChars + 1) };

        var build = new SynapseCompiler(ReferenceSet.FromDefaultContext()).Compile(new SynapseSource(Key, files, null));

        Assert.False(build.Success);
        Assert.Contains(build.Diagnostics, d => d.Id == "DB0047" && d.File == "spec.md");
    }

    [Fact]
    public Task AnUnlistedNamespaceIsRefused() =>
        Refuses("System.Xml.Linq.", "public static class Leak { public static string Shape() => new System.Xml.Linq.XElement(\"a\").ToString(); }");

    [Fact]
    public async Task ARefusedDeployKeepsTheStagedBuild()
    {
        var installs = new Installs();
        var deployment = new FakeDeployment(installs);
        var host = Host(installs, deployment);
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
        var host = Host(new Installs());
        var source = Source(SynapseCompilerFacts.Budget + "\n" + leak);
        Assert.True(host.Compiler.Compile(source).Success);

        var outcome = await host.Deploy(Brain("ba"), source, None, Ct);

        Assert.Equal(DeployStatus.Refused, outcome.Status);

        // The reason names the first disallowed reference; the one this case is about is among every reference the boundary refuses.
        var disallowed = AllowedReferences.Disallowed(host.Compiler.Compile(source).Assembly).ToList();
        Assert.True(disallowed.Any(d => d.Contains(named, StringComparison.Ordinal)), $"{named} not in: {string.Join(" | ", disallowed)}");
        var diagnostic = Assert.Single(outcome.Diagnostics, d => d.Id == "DB0045");
        Assert.Equal(Key, diagnostic.File);
        Assert.Empty(await host.List(Brain("ba"), Ct));
    }

    private static BrainId Brain(string name) => NeuronId.Parse($"{name}/x/y").Brain;

    private static SynapseSource Source(string text) => new(Key, new Dictionary<string, string> { ["Budget.cs"] = text }, null);

    // The analyzer is left out, so only the host's own check stands in front of the load.
    private static UnitDeployer Host(Installs installs, FakeDeployment? deployment = null)
    {
        var loader = new UnitLoader();
        var compiler = new SynapseCompiler(ReferenceSet.FromDefaultContext(), [], withoutBannedApis: true);
        var sources = new FakeSources();
        var dep = deployment ?? new FakeDeployment(installs);
        return new UnitDeployer(compiler, loader, _ => sources, _ => dep);
    }

    private sealed class NoBindings : Dictionary<string, IReadOnlyDictionary<string, string>>;

    private sealed class FakeSources : ISources
    {
        public Task<AliasConflict?> Claim(string sourceKey, IReadOnlyList<string> aliases, CancellationToken ct = default) => Task.FromResult<AliasConflict?>(null);
        public Task Release(string sourceKey, IReadOnlyList<string> aliases, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string?> OwnerOf(string alias, CancellationToken ct = default) => Task.FromResult<string?>("Me.Budget");
        public Task<IReadOnlyList<string>> Keys(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["Me.Budget"]);
    }

    private sealed class FakeDeployment(Installs installs) : IUnitDeployment
    {
        public StoredBuild? LastBuild { get; private set; }
        public StoredBuild? StagedBuild { get; private set; }

        public Task<DeployOutcome> Propose(StoredBuild build, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> bindings, CancellationToken ct = default)
        {
            if (installs.Ask)
            {
                StagedBuild = build;
                var p = build.Aliases.ToDictionary(a => a, a => new InstallOutcome(InstallStatus.Pending, "", "plan-1"));
                return Task.FromResult(new DeployOutcome(DeployStatus.Staged, build.Ref.Hash, [], p, null));
            }
            LastBuild = build;
            var outcomes = build.Aliases.ToDictionary(a => a, a => installs.Install(new InstallRequest(a, new Dictionary<string, string>()), ct).Result);
            return Task.FromResult(new DeployOutcome(DeployStatus.Deployed, build.Ref.Hash, [], outcomes, null));
        }

        public Task<BuildRef?> Ref(UnitView view, CancellationToken ct = default) =>
            Task.FromResult<BuildRef?>(view == UnitView.Proposed && StagedBuild != null ? StagedBuild.Ref : LastBuild?.Ref);
        public Task<StoredBuild?> Build(string hash, CancellationToken ct = default) => Task.FromResult(LastBuild?.Ref.Hash == hash ? LastBuild : null);
        public Task<UnitSourceView?> Source(CancellationToken ct = default) =>
            Task.FromResult(LastBuild is not null ? new UnitSourceView(LastBuild.Ref.SourceKey, LastBuild.Ref.Hash, LastBuild.Files, LastBuild.Icon, LastBuild.Aliases, DateTimeOffset.UtcNow, LastBuild.RebuildReason) : null);
        public Task Refuse(string hash, string reason, CancellationToken ct = default) => Task.CompletedTask;
        public Task Retire(CancellationToken ct = default) => Task.CompletedTask;
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

        public Task Uninstall(string alias, CancellationToken ct = default) => Task.CompletedTask;

        public Task Rewire(string alias, CancellationToken ct = default) => Task.CompletedTask;

        public Task<InstallRecord?> Describe(string alias, CancellationToken ct) =>
            Task.FromResult<InstallRecord?>(new InstallRecord(alias, "", new Dictionary<string, string>(), [], DateTimeOffset.UnixEpoch));

        public Task<IReadOnlyList<InstallRecord>> List(CancellationToken ct) => Task.FromResult<IReadOnlyList<InstallRecord>>([]);

        public Task<IReadOnlyList<PendingInstall>> Pending(CancellationToken ct) => Task.FromResult<IReadOnlyList<PendingInstall>>([]);
    }
}
