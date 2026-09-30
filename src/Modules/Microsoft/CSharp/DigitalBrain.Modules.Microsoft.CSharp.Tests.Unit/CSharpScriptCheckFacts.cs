using DigitalBrain.Microsoft.CSharp;
using Xunit;

namespace DigitalBrain.Microsoft.CSharp.Tests;

public sealed class CSharpScriptCheckFacts
{
    private readonly CSharpScriptCheck _check = new();

    [Fact]
    public void ACompilingScriptWithFileDirectivesAndContractsPasses()
    {
        var result = _check.Check(new Dictionary<string, string>
        {
            ["behaviors/hello.cs"] = """
                #:project /brain/src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp.Contracts/DigitalBrain.Modules.Microsoft.CSharp.Contracts.csproj
                var file = default(DigitalBrain.Microsoft.CSharp.ICSharpFile);
                Console.WriteLine($"{args.Length} {file}");
                await Task.CompletedTask;
                """,
        });

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => $"{error.Id} {error.Message}")));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ATypeErrorNamesTheFileTheLineAndTheDiagnostic()
    {
        var result = _check.Check(new Dictionary<string, string>
        {
            ["tests.cs"] = "var ok = 1;\nint wrong = \"text\";\n",
        });

        Assert.False(result.Success);
        var error = Assert.Single(result.Errors);
        Assert.Equal(("tests.cs", "CS0029", 2), (error.File, error.Id, error.Line));
    }

    [Fact]
    public void CheckingNothingIsRefused()
        => Assert.Throws<ArgumentException>(() => _check.Check(new Dictionary<string, string>()));
}
