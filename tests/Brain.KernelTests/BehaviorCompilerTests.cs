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
}
