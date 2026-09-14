using System.Reflection;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Coding;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class CodeWorkspaceNeuronFacts
{
    private static Task<BrainSimulation> StartAsync() => BrainSimulation.StartAsync(new()
    {
        Modules = new([typeof(CodingModule)]),
        ConfigureSilo = silo => silo.Services.AddSingleton<ISolutionLoader>(new AdhocSolutionLoader(FixtureSolutions.TwoProjects)),
    });

    private static async Task<WorkspaceSnapshot> WaitAsync(ICodeWorkspace workspace, WorkspacePhase phase)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            var snapshot = await workspace.Read();
            if (snapshot.Phase == phase)
            {
                return snapshot;
            }

            await Task.Delay(25, timeout.Token);
        }
    }

    [Theory]
    [InlineData(typeof(ICodeWorkspace), 12)]
    [InlineData(typeof(IChangeSet), 5)]
    public void Every_contract_method_uses_section_7_types(Type contract, int expectedMethods)
    {
        var options = DescriptorTable.ContractOptions(CodingJson.Default);
        var methods = contract.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach (var method in methods)
        {
            var argumentTypes = method.GetParameters()
                .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
                .Select(parameter => parameter.ParameterType);
            var resultType = method.ReturnType.GetGenericArguments()[0];
            foreach (var type in argumentTypes.Append(resultType))
            {
                DescriptorRules.ValidateMemberTypes(method, options.GetTypeInfo(type));
            }
        }

        Assert.Equal(expectedMethods, methods.Length);
    }

    [Fact]
    public async Task An_unopened_workspace_reads_as_not_opened()
    {
        await using var brain = await StartAsync();
        var workspace = brain.Grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, "fixture").ToGrainId());
        var snapshot = await workspace.Read();
        Assert.Equal(WorkspacePhase.NotOpened, snapshot.Phase);
        Assert.Null(snapshot.SolutionPath);
    }

    [Fact]
    public async Task A_warmed_solution_reads_ready_before_any_open()
    {
        await using var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(CodingModule)]),
            ConfigureSilo = silo => silo.Services.AddSingleton<ISolutionLoader>(new AdhocSolutionLoader(FixtureSolutions.TwoProjects)),
            Configuration = new Dictionary<string, string?>
            {
                [CodingModule.SolutionPathKey] = "E:/fixture/Fixture.slnx",
                [CodingModule.WorkspaceKeyKey] = "fixture",
            },
        });
        await brain.SiloServices.GetRequiredService<SolutionWorkspace>().WhenReadyAsync(TestContext.Current.CancellationToken);
        var workspace = brain.Grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, "fixture").ToGrainId());
        var snapshot = await workspace.Read();
        Assert.Equal(WorkspacePhase.Ready, snapshot.Phase);
        Assert.Equal(2, snapshot.ProjectCount);
        Assert.Equal(Path.GetFullPath("E:/fixture/Fixture.slnx"), snapshot.SolutionPath);
        // The service is ready as soon as its own load finishes; the grain's own generation-1 record of that
        // open runs as a separate, best-effort background call, so it lands a little later than the service does.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (snapshot.Generation < 1)
        {
            await Task.Delay(25, timeout.Token);
            snapshot = await workspace.Read();
        }

        Assert.Equal(1, snapshot.Generation);
    }

    [Fact]
    public async Task Reload_after_a_warmed_start_records_the_solution()
    {
        await using var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(CodingModule)]),
            ConfigureSilo = silo => silo.Services.AddSingleton<ISolutionLoader>(new AdhocSolutionLoader(FixtureSolutions.TwoProjects)),
            Configuration = new Dictionary<string, string?> { [CodingModule.SolutionPathKey] = "E:/fixture/Fixture.slnx" },
        });
        await brain.SiloServices.GetRequiredService<SolutionWorkspace>().WhenReadyAsync(TestContext.Current.CancellationToken);
        var workspace = brain.Grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, "fixture").ToGrainId());
        await workspace.Reload(new ReloadWorkspace(CommandId.New()));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        WorkspaceSnapshot snapshot;
        do
        {
            await Task.Delay(25, timeout.Token);
            snapshot = await workspace.Read();
        } while (snapshot.Generation != 1 || snapshot.Phase != WorkspacePhase.Ready);
        Assert.Equal(Path.GetFullPath("E:/fixture/Fixture.slnx"), snapshot.SolutionPath);
    }

    [Fact]
    public async Task Open_is_accepted_and_the_workspace_becomes_ready()
    {
        await using var brain = await StartAsync();
        var workspace = brain.Grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, "fixture").ToGrainId());
        var accepted = await workspace.Open(new OpenWorkspace(CommandId.New(), "E:/fixture/Fixture.slnx"));
        Assert.Equal("fixture", accepted.Receipt.Key);
        var ready = await WaitAsync(workspace, WorkspacePhase.Ready);
        Assert.Equal("E:/fixture/Fixture.slnx", ready.SolutionPath);
        Assert.Equal(2, ready.ProjectCount);
        Assert.Equal(1, ready.Generation);
    }

    [Fact]
    public async Task Open_refuses_a_blank_path_with_advice()
    {
        await using var brain = await StartAsync();
        var workspace = brain.Grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, "fixture").ToGrainId());
        var error = await Assert.ThrowsAnyAsync<Exception>(() => workspace.Open(new OpenWorkspace(CommandId.New(), " ")));
        Assert.Contains("solution path", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Queries_go_through_the_neuron()
    {
        await using var brain = await StartAsync();
        var workspace = brain.Grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, "fixture").ToGrainId());
        await workspace.Open(new OpenWorkspace(CommandId.New(), "E:/fixture/Fixture.slnx"));
        await WaitAsync(workspace, WorkspacePhase.Ready);
        var symbols = await workspace.FindSymbols(new("Greeter"), TestContext.Current.CancellationToken);
        var type = Assert.Single(symbols.Items, hit => hit.Kind == "NamedType");
        var references = await workspace.References(new(type.Id), TestContext.Current.CancellationToken);
        // Program.cs constructs a Greeter; Shouter.cs names it in "class Shouter : Greeter".
        Assert.Equal(2, references.Items.Count);
        var diagnostics = await workspace.Diagnostics(new(Path: FixtureSolutions.BrokenPath), TestContext.Current.CancellationToken);
        Assert.Equal(1, diagnostics.ErrorCount);
        var map = await workspace.Map(new(), TestContext.Current.CancellationToken);
        Assert.Equal(2, map.Projects.Count);
    }

    [Fact]
    public async Task A_query_before_open_is_advice()
    {
        await using var brain = await StartAsync();
        var workspace = brain.Grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, "fixture").ToGrainId());
        var error = await Assert.ThrowsAnyAsync<Exception>(() => workspace.FindSymbols(new("Greeter"), TestContext.Current.CancellationToken));
        Assert.Contains("No solution is open", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reload_bumps_the_generation_and_stays_ready()
    {
        await using var brain = await StartAsync();
        var workspace = brain.Grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, "fixture").ToGrainId());
        await workspace.Open(new OpenWorkspace(CommandId.New(), "E:/fixture/Fixture.slnx"));
        await WaitAsync(workspace, WorkspacePhase.Ready);
        await workspace.Reload(new ReloadWorkspace(CommandId.New()));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        WorkspaceSnapshot snapshot;
        do
        {
            await Task.Delay(25, timeout.Token);
            snapshot = await workspace.Read();
        } while (snapshot.Generation < 2 || snapshot.Phase != WorkspacePhase.Ready);
        Assert.Equal(2, snapshot.Generation);
    }

    [Fact]
    public async Task The_warmup_opens_the_grain_so_its_state_records_the_solution()
    {
        await using var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(CodingModule)]),
            ConfigureSilo = silo => silo.Services.AddSingleton<ISolutionLoader>(new AdhocSolutionLoader(FixtureSolutions.TwoProjects)),
            Configuration = new Dictionary<string, string?>
            {
                [CodingModule.SolutionPathKey] = "E:/fixture/Fixture.slnx",
                [CodingModule.WorkspaceKeyKey] = "fixture",
            },
        });
        var workspace = brain.Grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, "fixture").ToGrainId());
        var snapshot = await WaitAsync(workspace, WorkspacePhase.Ready);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (snapshot.Generation < 1)
        {
            await Task.Delay(50, timeout.Token);
            snapshot = await workspace.Read();
        }

        Assert.Equal(1, snapshot.Generation);
        Assert.Equal(Path.GetFullPath("E:/fixture/Fixture.slnx"), snapshot.SolutionPath);
    }

    [Fact]
    public async Task The_map_answers_from_the_durable_cache_while_a_reload_is_in_flight()
    {
        var gate = new TaskCompletionSource();
        var opens = 0;
        var loader = new GatedSecondOpenLoader(FixtureSolutions.TwoProjects, gate, () => opens++);
        await using var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(CodingModule)]),
            ConfigureSilo = silo => silo.Services.AddSingleton<ISolutionLoader>(loader),
        });
        var workspace = brain.Grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, "fixture").ToGrainId());
        await workspace.Open(new OpenWorkspace(CommandId.New(), "E:/fixture/Fixture.slnx"));
        await WaitAsync(workspace, WorkspacePhase.Ready);
        var first = await workspace.Map(new(), TestContext.Current.CancellationToken);
        Assert.Equal(2, first.Projects.Count);

        // The gated second open keeps the service Opening indefinitely (until gate.SetResult() below), but
        // LastMap is not on the snapshot, so the cache is proven indirectly: Map() is retried past any
        // WorkspaceNotReadyException until the mapping reaction's save of the first load's map lands, and
        // Phase/Map() are read together so the fact cannot pass because the reload quietly finished instead.
        await workspace.Reload(new ReloadWorkspace(CommandId.New()));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        WorkspaceSnapshot snapshot;
        SolutionMap? cached;
        while (true)
        {
            snapshot = await workspace.Read();
            try
            {
                cached = await workspace.Map(new(), TestContext.Current.CancellationToken);
            }
            catch (WorkspaceNotReadyException)
            {
                cached = null;
            }

            if (snapshot.Phase == WorkspacePhase.Opening && cached is not null)
            {
                break;
            }

            await Task.Delay(25, timeout.Token);
        }

        Assert.Equal(WorkspacePhase.Opening, snapshot.Phase);
        Assert.Equal(2, cached!.Projects.Count);

        gate.SetResult();
        await WaitAsync(workspace, WorkspacePhase.Ready);
    }

    // The first open completes at once; the second waits for the gate so a fact can observe "Opening".
    private sealed class GatedSecondOpenLoader(Func<global::Microsoft.CodeAnalysis.Workspace> open, TaskCompletionSource gate, Action opened) : ISolutionLoader
    {
        private int _opens;

        public async Task<LoadedSolution> OpenAsync(string solutionPath, IProgress<string> progress, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _opens) > 1)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            opened();
            return new LoadedSolution(open(), []);
        }
    }
}
