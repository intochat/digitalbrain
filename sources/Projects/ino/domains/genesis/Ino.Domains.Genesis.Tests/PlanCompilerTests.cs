using Ino.Core;
using Ino.Domains.Genesis.Compilation;
using Microsoft.CodeAnalysis.Scripting;
using Xunit;

namespace Ino.Domains.Genesis.Tests;

/// <summary>
/// Unit tests for the Roslyn scripting wrapper that backs the L1 loop's
/// runtime neuron compilation. No Orleans cluster needed — the
/// compiler runs in-process.
/// </summary>
public sealed class PlanCompilerTests
{
    [Fact]
    public void Validate_returns_null_for_well_formed_body()
    {
        var body = "return NeuronResult.Ok($\"hello {Prompt}\");";
        Assert.Null(PlanCompiler.Validate(body));
    }

    [Fact]
    public void Validate_returns_diagnostic_for_syntax_error()
    {
        var body = "return NeuronResult Ok($\"hello\");"; // missing dot
        var error = PlanCompiler.Validate(body);
        Assert.NotNull(error);
        Assert.Contains("error", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_returns_diagnostic_for_unknown_symbol()
    {
        var body = "return TotallyMadeUpType.DoStuff();";
        var error = PlanCompiler.Validate(body);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task Execute_returns_neuron_result_with_globals_in_scope()
    {
        var body = "return NeuronResult.Ok($\"echo: {Prompt} (id={NeuronId}, user={UserId})\");";
        var result = await PlanCompiler.ExecuteAsync(
            body,
            new RoslynPlanGlobals
            {
                Prompt = "say hello",
                NeuronId = "genesis.echo",
                UserId = "u-1",
                CorrelationId = "corr-1",
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains("echo: say hello", result.Message);
        Assert.Contains("genesis.echo", result.Message);
        Assert.Contains("u-1", result.Message);
    }

    [Fact]
    public async Task Execute_propagates_compilation_errors_as_exception()
    {
        var body = "return NeuronResult Ok();";
        await Assert.ThrowsAsync<CompilationErrorException>(
            () => PlanCompiler.ExecuteAsync(
                body,
                new RoslynPlanGlobals { Prompt = "x" },
                TestContext.Current.CancellationToken));
    }
}
