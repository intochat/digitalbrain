using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DigitalBrain.Kernel.Creator;

public sealed class StepCompileStage
{
    public bool Compile(string code, out IReadOnlyList<string> errors)
    {
        var compilation = CSharpCompilation.Create(
            $"DigitalBrain.Creator.Steps.{Guid.NewGuid():N}",
            [CSharpSyntaxTree.ParseText(code)],
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errorList = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage())
            .ToArray();
        errors = errorList;
        return errorList.Length == 0;
    }
}
