using DigitalBrain.Coding;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CodeDiagnosticFacts
{
    [Fact]
    public void CompilerDiagnosticsRetainLocationAndRemoveHostPath()
    {
        var line = @"C:\private\build\Program.cs(15,62): error CS0103: The name 'tick' does not exist [C:\private\Behavior.csproj]";
        var diagnostic = Assert.Single(CodeValidationException.Parse("Build", line + "\n" + line));
        Assert.Equal("CS0103", diagnostic.Code);
        Assert.Equal("Program.cs", diagnostic.File);
        Assert.Equal(15, diagnostic.Line);
        Assert.Equal(62, diagnostic.Column);
        Assert.Equal("The name 'tick' does not exist", diagnostic.Message);
    }
}