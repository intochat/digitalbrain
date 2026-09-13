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

    [Fact]
    public void Every_contract_method_uses_section_7_types()
    {
        var options = DescriptorTable.ContractOptions(CodingJson.Default);
        var methods = typeof(ICodeWorkspace).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach (var method in methods)
        {
            var parameterTypes = method.GetParameters()
                .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
                .Select(parameter => parameter.ParameterType)
                .ToArray();

            if (parameterTypes.Length == 1)
            {
                DescriptorRules.ValidateMemberTypes(method, options.GetTypeInfo(parameterTypes[0]));
            }

            var returnType = method.ReturnType.GetGenericArguments()[0];
            DescriptorRules.ValidateMemberTypes(method, options.GetTypeInfo(returnType));
        }

        Assert.True(methods.Length >= 7, $"Expected at least seven declared methods on {nameof(ICodeWorkspace)}, found {methods.Length}.");
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
        Assert.Single(references.Items);
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
}
