using Brain.Modules.Behaviors;
using Xunit;

namespace Brain.KernelTests;

public class BehaviorCompilerTests
{
    [Fact]
    public void Valid_source_compiles_clean()
    {
        var result = BehaviorCompiler.Check("using System; Console.WriteLine(1 + 1);", []);

        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Source_with_a_type_error_fails_closed()
    {
        var result = BehaviorCompiler.Check("int x = \"nope\";", []);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void Source_referencing_an_undefined_symbol_fails_closed()
    {
        var result = BehaviorCompiler.Check("NoSuchType.DoesNotExist();", []);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void Compiles_a_real_behavior_body_against_the_brain_api()
    {
        var refs = new[]
        {
            typeof(Brain.Client.BrainCluster).Assembly.Location,
            typeof(Brain.Contracts.INeuronContract).Assembly.Location,
            typeof(Brain.Modules.Workspace.IChat).Assembly.Location,
        };
        var source = """
            using Brain.Client;
            using Brain.Modules.Workspace;
            await using var brain = await BrainCluster.Connect(System.Array.Empty<string>());
            var chat = brain.Get<IChat>("local-owner|behavior/x|behavior/x");
            await chat.PostAsync(new ChatPost("hi"));
            """;
        var result = BehaviorCompiler.Check(source, refs);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
    }

    [Fact]
    public void Rejects_a_behavior_calling_a_nonexistent_contract_member()
    {
        var refs = new[]
        {
            typeof(Brain.Client.BrainCluster).Assembly.Location,
            typeof(Brain.Contracts.INeuronContract).Assembly.Location,
            typeof(Brain.Modules.Workspace.IChat).Assembly.Location,
        };
        var source = """
            using Brain.Client;
            using Brain.Modules.Workspace;
            await using var brain = await BrainCluster.Connect(System.Array.Empty<string>());
            var chat = brain.Get<IChat>("local-owner|behavior/x|behavior/x");
            await chat.NoSuchMethodAsync("hi");
            """;
        var result = BehaviorCompiler.Check(source, refs);
        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
    }
}
