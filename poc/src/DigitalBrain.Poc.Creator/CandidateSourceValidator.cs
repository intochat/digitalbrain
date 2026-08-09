using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DigitalBrain.Poc.Abstractions;
using DigitalBrain.Poc.Charting.Contracts;
using DigitalBrain.Poc.Social.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Orleans;

namespace DigitalBrain.Poc.Creator;

public sealed class CandidateSourceValidator
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly IReadOnlySet<string> ApprovedPlatformReferences = new HashSet<string>(
        [
            "netstandard.dll",
            "System.Private.CoreLib.dll",
            "System.Runtime.dll",
            "System.Threading.dll",
            "System.Threading.Tasks.dll",
        ],
        StringComparer.OrdinalIgnoreCase);

    public CandidateValidationResult Validate(ElonChartAuthoringIntent intent, string source)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(source);
        return ValidateDecodedSource(intent, source);
    }

    public CandidateValidationResult Validate(ElonChartAuthoringIntent intent, byte[] sourceBytes)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(sourceBytes);
        var expectedHeader = FixedCandidateHeader.CreateUtf8(intent.Family);
        if (!sourceBytes.AsSpan().StartsWith(expectedHeader))
        {
            return Reject(CandidatePolicyError.FixedHeaderMismatch, "The candidate byte header is not exact.");
        }

        try
        {
            return ValidateDecodedSource(intent, StrictUtf8.GetString(sourceBytes));
        }
        catch (DecoderFallbackException)
        {
            return Reject(CandidatePolicyError.FixedHeaderMismatch, "The candidate source is not strict UTF-8.");
        }
    }

    private static CandidateValidationResult ValidateDecodedSource(
        ElonChartAuthoringIntent intent,
        string source)
    {
        var header = FixedCandidateHeader.Create(intent.Family);
        if (!HasExactHeader(source, header))
        {
            return Reject(CandidatePolicyError.FixedHeaderMismatch, "The candidate header is not exact.");
        }

        var tree = CSharpSyntaxTree.ParseText(
            source[header.Length..],
            ParseOptions,
            path: "elon-chart.cs");
        var root = tree.GetCompilationUnitRoot();
        var syntaxFailure = ValidateSyntax(intent, root);
        if (syntaxFailure is not null)
        {
            return syntaxFailure;
        }

        var compilation = CreateCompilation(intent, tree);
        var semanticFailure = ValidateSemantics(intent, compilation, tree, root);
        if (semanticFailure is not null)
        {
            return semanticFailure;
        }

        var diagnostics = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (diagnostics.Length != 0)
        {
            return Reject(
                CandidatePolicyError.CompilationError,
                string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString())));
        }

        var expected = ElonChartSyntaxFactory.BuildRoot(intent)
            .NormalizeWhitespace(indentation: "    ", eol: "\n")
            .ToFullString();
        var actual = root.NormalizeWhitespace(indentation: "    ", eol: "\n").ToFullString();
        return string.Equals(actual, expected, StringComparison.Ordinal)
            ? CandidateValidationResult.Valid
            : Reject(CandidatePolicyError.InvalidShape, "The candidate does not have the generated module shape.");
    }

    private static bool HasExactHeader(string source, string expected)
    {
        if (source.Contains('\r', StringComparison.Ordinal) ||
            !source.StartsWith(expected, StringComparison.Ordinal))
        {
            return false;
        }

        var expectedDirectives = expected.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var actualDirectives = source.Split('\n')
            .Where(line => line.TrimStart().StartsWith("#:", StringComparison.Ordinal))
            .ToArray();
        return actualDirectives.SequenceEqual(expectedDirectives, StringComparer.Ordinal);
    }

    private static CandidateValidationResult? ValidateSyntax(
        ElonChartAuthoringIntent intent,
        CompilationUnitSyntax root)
    {
        if (root.DescendantNodes().Any(node => node is
            TypeOfExpressionSyntax or
            ForStatementSyntax or
            ForEachStatementSyntax or
            ForEachVariableStatementSyntax or
            WhileStatementSyntax or
            DoStatementSyntax or
            UnsafeStatementSyntax or
            PointerTypeSyntax or
            FunctionPointerTypeSyntax or
            StackAllocArrayCreationExpressionSyntax or
            ImplicitStackAllocArrayCreationExpressionSyntax or
            GlobalStatementSyntax) ||
            root.DescendantTokens().Any(token => token.IsKind(SyntaxKind.UnsafeKeyword)) ||
            root.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Any(name => name.Identifier.ValueText == "dynamic"))
        {
            return Reject(CandidatePolicyError.ForbiddenConstruct, "The candidate contains forbidden syntax.");
        }

        if (root.DescendantNodes().OfType<ConstructorDeclarationSyntax>()
                .Any(constructor => constructor.Modifiers.Any(SyntaxKind.StaticKeyword)) ||
            root.DescendantNodes().OfType<FieldDeclarationSyntax>().Any(field =>
                field.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                field.Declaration.Variables.Any(variable => variable.Initializer is not null)))
        {
            return Reject(CandidatePolicyError.ForbiddenConstruct, "Static initialization is forbidden.");
        }

        var attributeNames = root.DescendantNodes().OfType<AttributeSyntax>()
            .Select(attribute => attribute.Name.ToString())
            .ToArray();
        if (attributeNames.Any(name => name.EndsWith("ModuleInitializer", StringComparison.Ordinal) ||
            name.EndsWith("ModuleInitializerAttribute", StringComparison.Ordinal)))
        {
            return Reject(CandidatePolicyError.ForbiddenConstruct, "Module initializers are forbidden.");
        }

        if (attributeNames.Any(name => name.EndsWith("DllImport", StringComparison.Ordinal) ||
            name.EndsWith("DllImportAttribute", StringComparison.Ordinal)))
        {
            return Reject(CandidatePolicyError.ForbiddenSymbol, "Native imports are forbidden.");
        }

        if (HasForbiddenName(root))
        {
            return Reject(CandidatePolicyError.ForbiddenSymbol, "The candidate names a forbidden symbol.");
        }

        var creations = root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().ToArray();
        if (creations.Any(creation => creation.Type.ToString() == "SocialPostObserved"))
        {
            return Reject(CandidatePolicyError.UnauthorizedOutput, "A trusted input cannot be emitted.");
        }

        var constructors = ValidateConstructors(root);
        if (constructors is not null)
        {
            return constructors;
        }

        var triggers = root.DescendantNodes().OfType<GenericNameSyntax>()
            .Where(name => name.Identifier.ValueText == "IHandle")
            .ToArray();
        if (triggers.Length != 2 ||
            triggers.Any(trigger => trigger.TypeArgumentList.Arguments.Count != 1))
        {
            return Reject(CandidatePolicyError.UnauthorizedTrigger, "A handler has a malformed trigger.");
        }

        var aliasAttributes = root.DescendantNodes().OfType<AttributeSyntax>()
            .Where(attribute => attribute.Name.ToString() == "Alias")
            .ToArray();
        if (aliasAttributes.Any(attribute =>
            attribute.ArgumentList?.Arguments is not { Count: 1 } arguments ||
            arguments[0].Expression is not LiteralExpressionSyntax))
        {
            return Reject(CandidatePolicyError.AliasCollision, "A local alias declaration is malformed.");
        }

        var aliases = aliasAttributes
            .Select(attribute => (LiteralExpressionSyntax)attribute.ArgumentList!.Arguments[0].Expression)
            .Select(literal => literal.Token.ValueText);
        if (aliases.Any(CandidateSemanticPolicy.TrustedAliases.Contains))
        {
            return Reject(CandidatePolicyError.AliasCollision, "A local alias collides with a trusted module.");
        }

        var chartCommands = creations.Where(creation => creation.Type.ToString() == "AddChartPoint").ToArray();
        if (chartCommands.Length != 1 ||
            chartCommands[0].ArgumentList?.Arguments.FirstOrDefault()?.Expression is not LiteralExpressionSyntax chart ||
            chart.Token.ValueText != intent.ChartId)
        {
            return Reject(CandidatePolicyError.UnauthorizedTarget, "The chart target is not the granted chart.");
        }

        return null;
    }

    private static CandidateValidationResult? ValidateConstructors(CompilationUnitSyntax root)
    {
        var neurons = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(type => type.BaseList?.Types.Any(baseType =>
                baseType.Type is IdentifierNameSyntax { Identifier.ValueText: "Neuron" } or
                GenericNameSyntax { Identifier.ValueText: "Neuron" }) == true)
            .ToArray();
        foreach (var neuron in neurons)
        {
            var constructors = neuron.Members.OfType<ConstructorDeclarationSyntax>().ToArray();
            var expected = neuron.Identifier.ValueText == "ElonPostRuleNeuron"
                ? new[] { "IDigitalBrain", "IDurableState<ElonPostRuleState>" }
                : new[] { "IDigitalBrain" };
            if (constructors.Length != 1 ||
                !constructors[0].Modifiers.Any(SyntaxKind.PublicKeyword) ||
                !constructors[0].ParameterList.Parameters
                    .Select(parameter => parameter.Type?.ToString() ?? string.Empty)
                    .SequenceEqual(expected, StringComparer.Ordinal))
            {
                return Reject(
                    CandidatePolicyError.ForbiddenConstructor,
                    "A neuron constructor requests an unapproved service.");
            }
        }

        return neurons.Length == 2
            ? null
            : Reject(CandidatePolicyError.ForbiddenConstructor, "The exact neuron constructors are missing.");
    }

    private static bool HasForbiddenName(CompilationUnitSyntax root)
    {
        var identifiers = root.DescendantTokens()
            .Where(token => token.IsKind(SyntaxKind.IdentifierToken) || token.IsKind(SyntaxKind.ObjectKeyword))
            .Select(token => token.ValueText)
            .ToHashSet(StringComparer.Ordinal);
        return identifiers.Overlaps(
        [
            "File",
            "HttpClient",
            "Process",
            "Environment",
            "Console",
            "Assembly",
            "GetType",
            "object",
            "IServiceProvider",
            "ServiceProvider",
            "IGrainFactory",
            "GrainFactory",
            "IGrainBase",
            "Run",
            "Timer",
            "Parallel",
        ]);
    }

    private static CandidateValidationResult? ValidateSemantics(
        ElonChartAuthoringIntent intent,
        CSharpCompilation compilation,
        SyntaxTree tree,
        CompilationUnitSyntax root)
    {
        var model = compilation.GetSemanticModel(tree, ignoreAccessibility: false);
        var candidateAssemblyName = $"DigitalBrain.Poc.Candidate.{intent.Family.Value}";
        var triggerFailure = ValidateResolvedTriggers(intent, model, root, candidateAssemblyName);
        if (triggerFailure is not null)
        {
            return triggerFailure;
        }

        var localMethods = new Dictionary<ISymbol, MethodDeclarationSyntax>(SymbolEqualityComparer.Default);
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (model.GetDeclaredSymbol(method) is { } symbol)
            {
                localMethods[symbol.OriginalDefinition] = method;
            }
        }

        var calls = new Dictionary<ISymbol, ISymbol[]>(SymbolEqualityComparer.Default);
        foreach (var method in localMethods)
        {
            calls[method.Key] = method.Value.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Select(invocation => model.GetSymbolInfo(invocation).Symbol?.OriginalDefinition)
                .Where(called => called is not null && localMethods.ContainsKey(called))
                .Cast<ISymbol>()
                .ToArray();
        }

        if (HasCallCycle(calls))
        {
            return Reject(CandidatePolicyError.RecursiveCall, "Candidate helper calls must be acyclic.");
        }

        var symbolNodes = root.DescendantNodes().Where(node => node is
            IdentifierNameSyntax or
            GenericNameSyntax or
            MemberAccessExpressionSyntax or
            InvocationExpressionSyntax or
            ObjectCreationExpressionSyntax or
            AttributeSyntax);
        foreach (var node in symbolNodes)
        {
            var symbol = model.GetSymbolInfo(node).Symbol;
            if (symbol is not null && !CandidateSemanticPolicy.IsApprovedSymbol(symbol, candidateAssemblyName))
            {
                return Reject(
                    CandidatePolicyError.ForbiddenSymbol,
                    $"Resolved symbol '{symbol.ToDisplayString()}' is not allowed.");
            }
        }

        return null;
    }

    private static CandidateValidationResult? ValidateResolvedTriggers(
        ElonChartAuthoringIntent intent,
        SemanticModel model,
        CompilationUnitSyntax root,
        string candidateAssemblyName)
    {
        var externalAliases = new List<string>();
        var localTriggers = 0;
        foreach (var handle in root.DescendantNodes().OfType<GenericNameSyntax>()
            .Where(name => name.Identifier.ValueText == "IHandle"))
        {
            if (handle.TypeArgumentList.Arguments.Count != 1 ||
                model.GetTypeInfo(handle.TypeArgumentList.Arguments[0]).Type is not INamedTypeSymbol trigger)
            {
                return Reject(CandidatePolicyError.UnauthorizedTrigger, "A handler trigger did not resolve.");
            }

            if (string.Equals(
                trigger.ContainingAssembly?.Name,
                candidateAssemblyName,
                StringComparison.Ordinal))
            {
                if (trigger.Name != "ElonPostMatched")
                {
                    return Reject(CandidatePolicyError.UnauthorizedTrigger, "A local handler trigger is not approved.");
                }

                localTriggers++;
                continue;
            }

            var alias = ResolvedAlias(trigger);
            if (alias is null)
            {
                return Reject(CandidatePolicyError.UnauthorizedTrigger, "A trusted handler trigger has no exact alias.");
            }

            externalAliases.Add(alias);
        }

        return localTriggers == 1 &&
            externalAliases.Count == 1 &&
            externalAliases[0] == intent.AttestedTriggerAlias
            ? null
            : Reject(
                CandidatePolicyError.UnauthorizedTrigger,
                "A resolved handler trigger is outside the attested grant.");
    }

    private static string? ResolvedAlias(INamedTypeSymbol type)
    {
        var aliases = type.GetAttributes()
            .Where(attribute => attribute.AttributeClass?.ToDisplayString() == "Orleans.AliasAttribute")
            .Where(attribute => attribute.ConstructorArguments.Length == 1)
            .Select(attribute => attribute.ConstructorArguments[0].Value)
            .OfType<string>()
            .ToArray();
        return aliases.Length == 1 ? aliases[0] : null;
    }

    private static bool HasCallCycle(IReadOnlyDictionary<ISymbol, ISymbol[]> calls)
    {
        var visiting = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        return calls.Keys.Any(Visit);

        bool Visit(ISymbol method)
        {
            if (visited.Contains(method))
            {
                return false;
            }

            if (!visiting.Add(method))
            {
                return true;
            }

            if (calls[method].Any(Visit))
            {
                return true;
            }

            visiting.Remove(method);
            visited.Add(method);
            return false;
        }
    }

    private static CSharpCompilation CreateCompilation(
        ElonChartAuthoringIntent intent,
        SyntaxTree tree)
    {
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        var platformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ??
            throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        foreach (var path in platformAssemblies.Split(Path.PathSeparator))
        {
            if (ApprovedPlatformReferences.Contains(Path.GetFileName(path)))
            {
                references[path] = MetadataReference.CreateFromFile(path);
            }
        }

        foreach (var assembly in new[]
        {
            typeof(Synapse).Assembly,
            typeof(SocialPostObserved).Assembly,
            typeof(AddChartPoint).Assembly,
            typeof(AliasAttribute).Assembly,
        })
        {
            references[assembly.Location] = MetadataReference.CreateFromFile(assembly.Location);
        }

        return CSharpCompilation.Create(
            $"DigitalBrain.Poc.Candidate.{intent.Family.Value}",
            [tree],
            references.Values,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: false,
                deterministic: true));
    }

    private static CandidateValidationResult Reject(CandidatePolicyError error, string detail) =>
        CandidateValidationResult.Reject(error, detail);
}
