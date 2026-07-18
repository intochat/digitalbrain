# Handoff Report — InoLang Roslyn Test Source Generator Design

## 1. Observation

During read-only exploration of the `digitalbrain` repository, the following codebases, files, and structures were observed:

### A. Roslyn Source Generator Structure (`BrainOS.Core.SourceGen`)
- **Path**: `kernel/BrainOS.Core.SourceGen/`
- **File**: `NeuronGenerator.cs` (lines 13-14, 68-117)
  ```csharp
  [Generator]
  public sealed class NeuronGenerator : IIncrementalGenerator
  {
      public void Initialize(IncrementalGeneratorInitializationContext context)
      {
          context.RegisterPostInitializationOutput(static ctx =>
              ctx.AddSource("NeuronAttribute.g.cs",
                  SourceText.From(AttributeSource, Encoding.UTF8)));

          var neurons = context.SyntaxProvider.ForAttributeWithMetadataName(
              fullyQualifiedMetadataName: AttributeFullName,
              predicate: static (node, _) => node is ClassDeclarationSyntax,
              transform: static (ctx, _) => TryBuildModel(ctx));
          ...
          context.RegisterSourceOutput(nonNull, static (spc, model) => { ... });
      }
  }
  ```
- **Dependencies**: The `BrainOS.Core.SourceGen.csproj` targets `netstandard2.0` (lines 3-5), uses the latest `LangVersion` (line 6), and includes the required `Microsoft.CodeAnalysis.CSharp` reference (lines 17-20).

### B. InoLang Compiler and Parser API (`DigitalBrain.InoLang`)
- **Path**: `inolang/DigitalBrain.InoLang/`
- **Compiler**: `InoCompiler.cs` (lines 35-51) shows the standard compilation pipeline:
  ```csharp
  public static class InoCompiler
  {
      public static CompiledNeuron Compile(string source, IContractCatalog catalog)
      {
          var bag = new DiagnosticBag();
          var tokens = new Lexer(source, bag).Lex();
          var doc = new Parser(tokens, bag).ParseDocument();
          if (doc is null || bag.HasErrors)
              return new CompiledNeuron(null, null, bag.Items);

          var linked = new Linker(catalog, bag).Link(doc);
          if (linked is null || bag.HasErrors)
              return new CompiledNeuron(null, null, bag.Items);

          return new CompiledNeuron(Lowering.Lower(linked), linked, bag.Items);
      }
  }
  ```
- **Parser**: `Parsing/Parser.cs` (lines 8-9, 42-78) shows that `Parser` parses a list of tokens into a `NeuronDoc` AST node containing a `Scenarios` list without requiring an `IContractCatalog` (which is only introduced later at the `Linker` stage):
  ```csharp
  public NeuronDoc? ParseDocument()
  {
      ...
      var scenarios = new List<ScenarioDecl>();
      SkipNewLines();
      while (Is(TokenKind.Scenario)) { scenarios.Add(ParseScenario()); SkipNewLines(); }

      return new NeuronDoc(fqn, intent, usings, counters, handlers, scenarios,
          new SourceSpan(start.Start, Cur.Span.End));
  }
  ```
- **AST Nodes**: `Ast/Scenarios.cs` (lines 5-14) defines `ScenarioDecl` and the concrete `ScenarioStep` types:
  ```csharp
  public abstract record ScenarioStep(SourceSpan Span);
  public sealed record GivenSeamReturns(string Port, Expr Value, SourceSpan Span) : ScenarioStep(Span);
  public sealed record GivenPredicate(CallExpr Subject, string Value, SourceSpan Span) : ScenarioStep(Span);
  public sealed record WhenInject(string Port, IReadOnlyList<NamedArg> Args, SourceSpan Span) : ScenarioStep(Span);
  public sealed record ThenSignalEmitted(string Port, string? WithField, Expr? WithValue, SourceSpan Span) : ScenarioStep(Span);
  public sealed record ThenResourceHas(string Port, Expr Value, SourceSpan Span) : ScenarioStep(Span);
  public sealed record ThenCounter(string Counter, long Value, SourceSpan Span) : ScenarioStep(Span);

  public sealed record ScenarioDecl(string Name, IReadOnlyList<ScenarioStep> Steps, SourceSpan Span);
  ```

### C. Current Test Runner & Scenario Discovery
- **Path**: `inolang/DigitalBrain.InoLang.TestRunner/`
- **File**: `InoScenarioProjection.cs` (lines 31-51) dynamically discovers `.ino` files in a given directory:
  ```csharp
  public static IEnumerable<TheoryDataRow<string, string, string>> Discover(string rootPath)
  {
      ...
      foreach (var absolute in InoFileDiscovery.Enumerate(absoluteRoot))
      {
          var relative = NormalizePath(Path.GetRelativePath(absoluteRoot, absolute));
          foreach (var row in ProjectFileRows(absolute, relative))
              yield return row;
      }
  }
  ```
- **File**: `InoScenarioProjection.cs` (lines 53-103) executes a specific scenario at runtime using the `ScenarioRunner`:
  ```csharp
  public static async Task<ScenarioRunReport> RunAsync(
      string rootPath,
      string relativePath,
      string scenarioName,
      string scenarioKey,
      IContractCatalog catalog,
      CancellationToken ct)
  ```
- **Existing Tests**: Test classes like `OnboardingProjectionTests.cs` (lines 25-37) currently call this dynamic discovery inside an xUnit `[Theory]` row test:
  ```csharp
  public static IEnumerable<TheoryDataRow<string, string, string>> SeamScenarios()
      => InoScenarioProjection.Discover(SeamSpecDir);

  [Theory]
  [MemberData(nameof(SeamScenarios))]
  public async Task Onboarding_scenario_passes(string relativePath, string scenarioName, string scenarioKey)
  {
      var report = await InoScenarioProjection.RunAsync(
          SeamSpecDir, relativePath, scenarioName, scenarioKey,
          SeamCatalog(), TestContext.Current.CancellationToken);

      Assert.True(report.Passed, report.Message);
  }
  ```

### D. Upcoming Syntax and Metadata Specifications
- **File**: `docs/v3/2026-05-21-inolang-roslyn-meta-language.md` describes the proposed transition to an inline-FQN syntax without alias `using` blocks and using `.ino-catalog.json` schema files (lines 240-283).
- **Embedded Catalog Resolution**: The spec introduces a `CompositeContractCatalog` folding `.ino-catalog.json` entries from referenced C# SDK packages.

---

## 2. Logic Chain

1. **Incremental Compilation Context**: In `BrainOS.Core.SourceGen` (and Roslyn generators in general), external file contents are read using `context.AdditionalTextsProvider` where files are registered in `.csproj` files as `<AdditionalFiles Include="**/*.ino" />`.
2. **Catalog-Free Parsing**: Since `Parser.ParseDocument()` under `DigitalBrain.InoLang.Parsing` parses token streams into `NeuronDoc` ASTs without requiring an external `IContractCatalog`, a Source Generator can run the `Lexer` and `Parser` in a lightweight manner to extract scenario names and positions.
3. **Class Attribute Mapping**: To run `.ino` scenarios, a `MapCatalog` (or composite `IContractCatalog`) is required at runtime to resolve types and symbols. To bridge the gap between generator-parsed `.ino` scenarios and developer-defined test catalogs, we can introduce a `[InoTestTarget("onboarding.ino")]` attribute. 
4. **Linking Generated Test Cases to Catalogs**:
   - The generator registers a `[InoTestTarget]` attribute class.
   - The test developer decorates a `partial class` (which defines their `IContractCatalog` provider method) with `[InoTestTarget("filename.ino")]`.
   - The generator finds this class, reads the matching `.ino` additional file, parses its scenarios, and emits the other half of the `partial class`.
   - For each scenario, the generator emits a static `[Fact]` test method that executes `InoScenarioProjection.RunAsync(...)` at test runtime using the developer's declared catalog method.
5. **Compile-Time Test Visibility**: By translating dynamically-discovered rows into static `[Fact]` test methods, every scenario is explicitly registered with xUnit at compile-time. This provides direct IDE test explorer navigation, specific scenario filters (e.g. `dotnet test --filter "DisplayName~..."`), and immediate feedback on exactly which scenario failed.

---

## 3. Caveats

1. **Source Gen Reference Loading**: The Roslyn source generator runs within the compiler process, which is restricted to `netstandard2.0`. Therefore, the `DigitalBrain.InoLang` project must support `netstandard2.0` (or the generator must use a shared subset of parser sources) to be successfully referenced and loaded inside the compiler assembly context.
2. **Path Normalization**: `AdditionalFiles` paths can be absolute or relative depending on host OS and MSBuild environment. The generator must normalize directory paths using `Path.GetFileName` and forward slashes to ensure deterministic matching.
3. **Duplicate Scenario Names**: If multiple scenarios in a `.ino` file have identical names, xUnit fact methods will duplicate names. The generator must append unique numerical indices (e.g., `Scenario_0`, `Scenario_1`) to the generated C# method names to avoid compiling errors, while preserving the user-facing `DisplayName`.

---

## 4. Conclusion

We propose the design and structure of **`InoTestGenerator`**, a Roslyn incremental source generator targeting `.ino` file scenario translation. It completely automates and optimizes test execution in Milestone 3 by translating raw `.ino` files directly into compile-time xUnit test facts.

### A. Generator Architecture (`InoTestGenerator.cs`)

The class will live in `BrainOS.Core.SourceGen` (or a dedicated test-specific analyzer package) and follow this incremental structure:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using DigitalBrain.InoLang.Lexing;
using DigitalBrain.InoLang.Parsing;
using DigitalBrain.InoLang.Diagnostics;

namespace BrainOS.Core.SourceGen;

[Generator]
public sealed class InoTestGenerator : IIncrementalGenerator
{
    private const string AttributeFullName = "DigitalBrain.InoLang.Testing.InoTestTargetAttribute";

    private const string AttributeSource = """
        // <auto-generated />
        #nullable enable
        namespace DigitalBrain.InoLang.Testing;

        [global::System.AttributeUsage(global::System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
        internal sealed class InoTestTargetAttribute : global::System.Attribute
        {
            public string InoFilePath { get; }
            public InoTestTargetAttribute(string inoFilePath)
            {
                InoFilePath = inoFilePath;
            }
        }
        """;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Post-initialize the targeting attribute
        context.RegisterPostInitializationOutput(static ctx =>
            ctx.AddSource("InoTestTargetAttribute.g.cs", SourceText.From(AttributeSource, Encoding.UTF8)));

        // 2. Discover test classes decorated with the attribute
        var testClasses = context.SyntaxProvider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: AttributeFullName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, _) => GetClassModel(ctx));

        // 3. Discover all AdditionalFiles matching *.ino
        var inoFiles = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".ino", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, ct) => new InoFileSource(
                FileName: Path.GetFileName(file.Path),
                FullPath: file.Path,
                Content: file.GetText(ct)?.ToString() ?? string.Empty));

        // 4. Combine targets with source files
        var combined = testClasses.Combine(inoFiles.Collect());

        // 5. Emit source-generated test classes
        context.RegisterSourceOutput(combined, static (spc, pair) =>
        {
            var classModel = pair.Left;
            var inoFilesList = pair.Right;
            if (classModel is null) return;

            // Find matching .ino file by name
            InoFileSource? matchingIno = null;
            foreach (var file in inoFilesList)
            {
                if (string.Equals(file.FileName, classModel.TargetInoName, StringComparison.OrdinalIgnoreCase))
                {
                    matchingIno = file;
                    break;
                }
            }

            if (matchingIno is null)
            {
                // Emit warning/error diagnostic about missing .ino file
                return;
            }

            var generatedCode = EmitTests(classModel, matchingIno);
            spc.AddSource(classModel.HintName, SourceText.From(generatedCode, Encoding.UTF8));
        });
    }

    private static ClassModel? GetClassModel(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol cls) return null;
        var decl = ctx.TargetNode as ClassDeclarationSyntax;
        if (decl is null) return null;

        // Verify partial modifier
        bool isPartial = decl.Modifiers.Any(m => m.ValueText == "partial");
        if (!isPartial) return null;

        var attr = ctx.Attributes.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == AttributeFullName);
        if (attr is null || attr.ConstructorArguments.Length != 1) return null;

        var targetInoName = attr.ConstructorArguments[0].Value as string;
        if (string.IsNullOrEmpty(targetInoName)) return null;

        var ns = cls.ContainingNamespace.IsGlobalNamespace ? null : cls.ContainingNamespace.ToDisplayString();
        var qualified = cls.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var hintName = qualified.Replace("global::", "").Replace('.', '_') + ".InoTests.g.cs";

        return new ClassModel(ns, cls.Name, targetInoName, hintName);
    }

    private static string EmitTests(ClassModel targetClass, InoFileSource inoSource)
    {
        // Parse .ino source using InoLang Lexer and Parser
        var bag = new DiagnosticBag();
        var tokens = new Lexer(inoSource.Content, bag).Lex();
        var parser = new Parser(tokens, bag);
        var doc = parser.ParseDocument();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Xunit;");
        sb.AppendLine();

        if (targetClass.Namespace is not null)
        {
            sb.Append("namespace ").Append(targetClass.Namespace).AppendLine(";");
            sb.AppendLine();
        }

        sb.Append("partial class ").AppendLine(targetClass.ClassName);
        sb.AppendLine("{");

        var rootDir = Path.GetDirectoryName(inoSource.FullPath).Replace("\\", "\\\\");
        var fileName = Path.GetFileName(inoSource.FullPath);

        if (doc is null || bag.HasErrors)
        {
            // Emit a failing test representing the compile error
            sb.AppendLine("    [Fact(DisplayName = \"" + fileName + " :: <compile error>\")]");
            sb.AppendLine("    public async Task Scenario_CompileError()");
            sb.AppendLine("    {");
            sb.AppendLine("        var catalog = GetCatalog();");
            sb.AppendLine($"        var report = await global::DigitalBrain.InoLang.TestRunner.InoScenarioProjection.RunAsync(");
            sb.AppendLine($"            @\"{rootDir}\", \"{fileName}\", \"<compile error>\", \"<compile-error>\",");
            sb.AppendLine($"            catalog, global::Xunit.TestContext.Current.CancellationToken);");
            sb.AppendLine("        global::Xunit.Assert.True(report.Passed, report.Message);");
            sb.AppendLine("    }");
        }
        else if (doc.Scenarios.Count == 0)
        {
            // Emit a failing test representing the no scenarios sentinel
            sb.AppendLine("    [Fact(DisplayName = \"" + fileName + " :: <no scenarios>\")]");
            sb.AppendLine("    public async Task Scenario_NoScenarios()");
            sb.AppendLine("    {");
            sb.AppendLine("        var catalog = GetCatalog();");
            sb.AppendLine($"        var report = await global::DigitalBrain.InoLang.TestRunner.InoScenarioProjection.RunAsync(");
            sb.AppendLine($"            @\"{rootDir}\", \"{fileName}\", \"{InoScenarioProjection.NoScenariosScenarioKey}\", \"{InoScenarioProjection.NoScenariosScenarioKey}\",");
            sb.AppendLine($"            catalog, global::Xunit.TestContext.Current.CancellationToken);");
            sb.AppendLine("        global::Xunit.Assert.True(report.Passed, report.Message);");
            sb.AppendLine("    }");
        }
        else
        {
            for (int i = 0; i < doc.Scenarios.Count; i++)
            {
                var scenario = doc.Scenarios[i];
                var escapedName = scenario.Name.Replace("\"", "\\\"");

                sb.AppendLine($"    [Fact(DisplayName = \"{fileName} :: {escapedName}\")]");
                sb.AppendLine($"    public async Task Scenario_{i}()");
                sb.AppendLine("    {");
                sb.AppendLine("        var catalog = GetCatalog();");
                sb.AppendLine($"        var report = await global::DigitalBrain.InoLang.TestRunner.InoScenarioProjection.RunAsync(");
                sb.AppendLine($"            @\"{rootDir}\", \"{fileName}\", \"{escapedName}\", \"scenario:{i}\",");
                sb.AppendLine($"            catalog, global::Xunit.TestContext.Current.CancellationToken);");
                sb.AppendLine("        global::Xunit.Assert.True(report.Passed, report.Message);");
                sb.AppendLine("    }");
                sb.AppendLine();
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private sealed record ClassModel(string? Namespace, string ClassName, string TargetInoName, string HintName);
    private sealed record InoFileSource(string FileName, string FullPath, string Content);
}
```

### B. Consumer Project Test Transition Plan

When using this new source generator inside a test project (e.g. `BrainOS.Domains.Onboarding.Tests`):

1. **Add AdditionalFiles reference in csproj**:
   ```xml
   <ItemGroup>
     <AdditionalFiles Include="Onboarding/onboarding.ino" />
   </ItemGroup>
   ```
2. **Simplify the manually written test class**:
   ```csharp
   using DigitalBrain.InoLang.Linking;
   using DigitalBrain.InoLang.TestKit;
   using DigitalBrain.InoLang.Testing;

   namespace BrainOS.Domains.Onboarding.Tests;

   [InoTestTarget("onboarding.ino")]
   public sealed partial class OnboardingProjectionTests
   {
       // Defining the catalog needed by InoScenarioProjection at test runtime.
       public static IContractCatalog GetCatalog() => MapCatalog.Empty()
           .With("BrainOS.Domains.Onboarding.Contracts.RequestOnboarding", ContractKind.Synapse, "UserId")
           .With("BrainOS.Domains.Onboarding.Contracts.AcceptPolicy", ContractKind.Synapse, "UserId", "Version")
           .With("BrainOS.Domains.Onboarding.Contracts.OnboardingResult", ContractKind.Signal, "NeedsAccept", "CurrentVersion")
           .With("BrainOS.Kernel.Contracts.Ui.RfwCard", ContractKind.Signal, "LibraryName", "RootWidget", "DataJson", "ReceiverNeuronType")
           .With("BrainOS.Domains.Onboarding.Contracts.PolicyAccepted", ContractKind.Signal, "UserId", "Version")
           .With("BrainOS.Domains.Onboarding.OnboardingNeuron", ContractKind.Neuron)
           .With("BrainOS.Domains.Onboarding.OnboardingStore", ContractKind.Neuron)
           .With("BrainOS.Kernel.Settings.SettingsStore", ContractKind.Neuron);
   }
   ```

---

## 5. Verification Method

To verify the `InoTestGenerator` after subsequent implementation:

1. **Build and package the Generator**:
   Run `dotnet build` on the generator project (`BrainOS.Core.SourceGen.csproj` or the newly defined generator assembly).
2. **Check compiler integration**:
   Build the consumer test project (e.g., `BrainOS.Domains.Onboarding.Tests`). Verify that the build completes successfully and that the generated `OnboardingProjectionTests.InoTests.g.cs` is emitted to the intermediate `obj/` folder under `obj/Debug/net11.0/generated/`.
3. **Verify in Test Explorer**:
   Run the test command:
   ```powershell
   dotnet test --filter "FullyQualifiedName~OnboardingProjectionTests"
   ```
   Confirm that all scenario facts (e.g. `onboarding.ino :: Scenario 1: First-use...` and `onboarding.ino :: Scenario 2: Policy...`) are discovered statically, run green, and can be addressed individually.
4. **Invalidation condition**:
   If an `.ino` file has a syntax error, the compiler should still build, but a failing fact test representing `Scenario_CompileError` will be emitted and fail with detailed compile diagnostics.
