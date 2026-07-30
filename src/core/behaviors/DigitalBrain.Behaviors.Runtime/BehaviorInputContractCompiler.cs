using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DigitalBrain.Behaviors;

internal static class BehaviorInputContractCompiler
{
    internal const string PolicyId = "contract-only-v1";
    internal const string SdkVersion = "11.0.100-preview.6";
    internal const string RoslynVersion = "5.6.0";
    internal const string LanguageVersionName = "Preview";

    internal static BehaviorCompilerPolicy DefaultPolicy { get; } = new(
        SdkVersion,
        RoslynVersion,
        LanguageVersionName,
        PolicyId);

    public static InputContractLoweringResult Lower(
        string programSource,
        BehaviorId behavior,
        ImmutableArray<MetadataReference> references,
        string? resultSchemaJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programSource);
        behavior.EnsureValid();

        var tree = CSharpSyntaxTree.ParseText(
            programSource,
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "Behavior.cs",
            encoding: Encoding.UTF8);

        var compilation = CSharpCompilation.Create(
            assemblyName: "Behavior.InputContract",
            syntaxTrees: [tree],
            references: references,
            options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    deterministic: true,
                    nullableContextOptions: NullableContextOptions.Enable)
                .WithMetadataImportOptions(MetadataImportOptions.All));

        var root = tree.GetCompilationUnitRoot();
        var model = compilation.GetSemanticModel(tree);
        var unions = FindRootUnions(root, model).ToArray();

        if (unions.Length == 0)
        {
            return InputContractLoweringResult.Fail(
                "A behavior must declare exactly one root input union.",
                DefaultPolicy);
        }

        if (unions.Length > 1)
        {
            var names = string.Join(", ", unions.Select(static item => item.Name).Order(StringComparer.Ordinal));
            return InputContractLoweringResult.Fail(
                $"A behavior cannot declare more than one root input union: {names}.",
                DefaultPolicy);
        }

        var union = unions[0];
        if (union.HasDefaultOrNullCase)
        {
            return InputContractLoweringResult.Fail(
                $"Root input union '{union.Name}' cannot declare default or null cases.",
                DefaultPolicy);
        }

        if (union.Cases.Count < 1)
        {
            return InputContractLoweringResult.Fail(
                $"Root input union '{union.Name}' must declare at least one case type.",
                DefaultPolicy);
        }

        var loweredCases = new List<BehaviorContractCaseManifest>(union.Cases.Count);
        var caseTypeKeys = new HashSet<string>(StringComparer.Ordinal);
        var symbols = new List<INamedTypeSymbol>(union.Cases.Count);

        foreach (var caseSyntax in union.Cases)
        {
            var type = model.GetTypeInfo(caseSyntax).Type as INamedTypeSymbol
                ?? model.GetSymbolInfo(caseSyntax).Symbol as INamedTypeSymbol;
            if (type is null)
            {
                return InputContractLoweringResult.Fail(
                    $"Union case '{caseSyntax}' could not be resolved to a named type.",
                    DefaultPolicy);
            }

            type = type.OriginalDefinition;
            if (IsUnionType(type, model, root))
            {
                return InputContractLoweringResult.Fail(
                    $"Nested unions are not supported; case '{type.Name}' is itself a union.",
                    DefaultPolicy);
            }

            if (!IsImmutableRecordShape(type))
            {
                return InputContractLoweringResult.Fail(
                    $"Union case '{type.Name}' must be an immutable record shape.",
                    DefaultPolicy);
            }

            var typeKey = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!caseTypeKeys.Add(typeKey))
            {
                return InputContractLoweringResult.Fail(
                    $"Union case '{type.Name}' is ambiguous or duplicated.",
                    DefaultPolicy);
            }

            foreach (var existing in symbols)
            {
                if (IsOverlappingCase(existing, type))
                {
                    return InputContractLoweringResult.Fail(
                        $"Union cases '{existing.Name}' and '{type.Name}' overlap.",
                        DefaultPolicy);
                }
            }

            symbols.Add(type);
            var caseName = type.Name;
            loweredCases.Add(new BehaviorContractCaseManifest(
                CaseId: $"case.{caseName}",
                CaseSchemaVersion: 1,
                CaseName: caseName,
                PayloadSchemaJson: PayloadSchemaJson(type)));
        }

        var orderedCases = loweredCases
            .OrderBy(static item => item.CaseId, StringComparer.Ordinal)
            .ToArray();
        var oneOf = OneOfSchemaJson(orderedCases);
        var resultSchema = string.IsNullOrWhiteSpace(resultSchemaJson)
            ? """{"type":"object"}"""
            : resultSchemaJson!;

        var contract = new BehaviorContractManifest(
            BehaviorContractId: behavior.Value,
            ContractMajorVersion: 1,
            OneOfSchemaJson: oneOf,
            Cases: orderedCases,
            ResultSchemaJson: resultSchema);

        return InputContractLoweringResult.Ok(
            contract,
            union.Name,
            orderedCases.Select(static item => item.CaseId).ToArray(),
            DefaultPolicy);
    }

    private static IEnumerable<UnionDeclarationShape> FindRootUnions(
        CompilationUnitSyntax root,
        SemanticModel model)
    {
        foreach (var member in root.Members)
        {
            if (TryReadUnionShape(member, out var shape))
            {
                yield return shape;
                continue;
            }

            // Namespace-wrapped single-file apps still expose one logical root.
            if (member is BaseNamespaceDeclarationSyntax ns)
            {
                foreach (var nested in ns.Members)
                {
                    if (TryReadUnionShape(nested, out shape))
                    {
                        yield return shape;
                    }
                }
            }
        }

        _ = model;
    }

    private static bool TryReadUnionShape(MemberDeclarationSyntax member, out UnionDeclarationShape shape)
    {
        shape = default!;
        var tokens = member.DescendantTokens()
            .Where(static token => !token.IsKind(SyntaxKind.None) && token.Text.Length > 0)
            .ToArray();
        if (tokens.Length == 0)
        {
            return false;
        }

        var unionIndex = -1;
        for (var index = 0; index < tokens.Length; index++)
        {
            var text = tokens[index].Text;
            if (text == "union")
            {
                unionIndex = index;
                break;
            }

            if (!IsModifierOrAttributeNoise(text) && text is not "[" and not "]" and not ",")
            {
                // Keep scanning a little for attribute lists / modifiers only.
                if (index > 8)
                {
                    break;
                }
            }
        }

        if (unionIndex < 0 || unionIndex + 1 >= tokens.Length)
        {
            return false;
        }

        var name = tokens[unionIndex + 1].Text;
        if (string.IsNullOrWhiteSpace(name) || name is "(" or ")" or ";" or ",")
        {
            return false;
        }

        var openIndex = -1;
        for (var index = unionIndex + 2; index < tokens.Length; index++)
        {
            if (tokens[index].Text == "(")
            {
                openIndex = index;
                break;
            }
        }

        if (openIndex < 0)
        {
            return false;
        }

        // Token-order case extraction preserves multiplicity and default/null markers.
        var caseTypes = new List<TypeSyntax>();
        var hasDefaultOrNull = false;
        var depth = 0;
        var current = new StringBuilder();
        for (var index = openIndex; index < tokens.Length; index++)
        {
            var text = tokens[index].Text;
            if (text == "(")
            {
                depth++;
                continue;
            }

            if (text == ")")
            {
                if (depth == 1)
                {
                    FlushCaseToken(current, caseTypes, ref hasDefaultOrNull);
                }

                depth--;
                if (depth == 0)
                {
                    break;
                }

                continue;
            }

            if (depth != 1)
            {
                continue;
            }

            if (text == ",")
            {
                FlushCaseToken(current, caseTypes, ref hasDefaultOrNull);
                continue;
            }

            if (text is "null" or "default" or "=")
            {
                hasDefaultOrNull = true;
                current.Clear();
                continue;
            }

            current.Append(text);
        }

        // Prefer semantic TypeSyntax nodes when token text matches an existing node.
        if (caseTypes.Count > 0)
        {
            var typeNodes = member.DescendantNodes()
                .OfType<TypeSyntax>()
                .Where(static type => type is IdentifierNameSyntax or QualifiedNameSyntax or AliasQualifiedNameSyntax or GenericNameSyntax)
                .ToArray();
            for (var index = 0; index < caseTypes.Count; index++)
            {
                var text = caseTypes[index].ToString();
                var match = typeNodes.FirstOrDefault(node => string.Equals(node.ToString(), text, StringComparison.Ordinal));
                if (match is not null)
                {
                    caseTypes[index] = match;
                }
            }
        }

        var memberText = member.ToFullString();
        if (ContainsDefaultOrNullCaseMarker(memberText))
        {
            hasDefaultOrNull = true;
        }

        shape = new UnionDeclarationShape(name, caseTypes, hasDefaultOrNull);
        return true;
    }

    private static bool ContainsDefaultOrNullCaseMarker(string memberText)
    {
        // Reject authoring of default/null cases even when the parser drops those tokens.
        for (var index = 0; index < memberText.Length; index++)
        {
            if (!IsIdentifierStart(memberText, index, "null") && !IsIdentifierStart(memberText, index, "default"))
            {
                continue;
            }

            var word = IsIdentifierStart(memberText, index, "null") ? "null" : "default";
            if (IsWholeWord(memberText, index, word.Length))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIdentifierStart(string text, int index, string word)
        => index + word.Length <= text.Length
            && string.Compare(text, index, word, 0, word.Length, StringComparison.Ordinal) == 0;

    private static bool IsWholeWord(string text, int index, int length)
    {
        if (index > 0 && (char.IsLetterOrDigit(text[index - 1]) || text[index - 1] == '_'))
        {
            return false;
        }

        var end = index + length;
        return end >= text.Length || (!char.IsLetterOrDigit(text[end]) && text[end] != '_');
    }

    private static void FlushCaseToken(
        StringBuilder current,
        List<TypeSyntax> caseTypes,
        ref bool hasDefaultOrNull)
    {
        if (current.Length == 0)
        {
            return;
        }

        var text = current.ToString();
        current.Clear();
        if (text is "null" or "default")
        {
            hasDefaultOrNull = true;
            return;
        }

        if (text == "union")
        {
            return;
        }

        caseTypes.Add(SyntaxFactory.ParseTypeName(text));
    }

    private static bool IsModifierOrAttributeNoise(string text)
        => text is "public" or "internal" or "private" or "protected" or "static" or "partial"
            or "file" or "sealed" or "abstract" or "new" or "readonly" or "ref" or "unsafe"
            or "required" or "[" or "]" or "(" or ")" or "," or ":" or "Attribute";

    private static bool IsUnionType(INamedTypeSymbol type, SemanticModel model, CompilationUnitSyntax root)
    {
        foreach (var member in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
        {
            if (!TryReadUnionShape(member, out var shape))
            {
                continue;
            }

            if (string.Equals(shape.Name, type.Name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        // Generated union structs implement System.Runtime.CompilerServices.IUnion and carry [Union].
        if (type.TypeKind == TypeKind.Struct
            && type.AllInterfaces.Any(static face => face.ToDisplayString() == "System.Runtime.CompilerServices.IUnion"))
        {
            return true;
        }

        if (type.GetAttributes().Any(static attribute =>
                attribute.AttributeClass?.Name is "Union" or "UnionAttribute"))
        {
            return true;
        }

        _ = model;
        return false;
    }

    private static bool IsImmutableRecordShape(INamedTypeSymbol type)
    {
        if (type.IsRecord)
        {
            return type.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(static property => property is { IsStatic: false, DeclaredAccessibility: Accessibility.Public })
                .All(static property => property.SetMethod is null
                    || property.SetMethod.IsInitOnly
                    || property.SetMethod.DeclaredAccessibility != Accessibility.Public);
        }

        // Non-record reference types are rejected as mutable payload shapes for this slice.
        return false;
    }

    private static bool IsOverlappingCase(INamedTypeSymbol left, INamedTypeSymbol right)
        => SymbolEqualityComparer.Default.Equals(left, right)
            || Inherits(left, right)
            || Inherits(right, left);

    private static bool Inherits(INamedTypeSymbol type, INamedTypeSymbol candidate)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static string PayloadSchemaJson(INamedTypeSymbol type)
    {
        var properties = type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(static property => property is
            {
                IsStatic: false,
                DeclaredAccessibility: Accessibility.Public,
                IsIndexer: false,
            } && property.Name != "EqualityContract")
            .OrderBy(static property => property.Name, StringComparer.Ordinal)
            .ToArray();

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WritePropertyName("properties");
            writer.WriteStartObject();
            foreach (var property in properties)
            {
                writer.WritePropertyName(property.Name);
                WriteJsonType(writer, property.Type);
            }

            writer.WriteEndObject();
            writer.WritePropertyName("required");
            writer.WriteStartArray();
            foreach (var property in properties)
            {
                writer.WriteStringValue(property.Name);
            }

            writer.WriteEndArray();
            writer.WriteBoolean("additionalProperties", false);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string OneOfSchemaJson(IReadOnlyList<BehaviorContractCaseManifest> cases)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("oneOf");
            writer.WriteStartArray();
            foreach (var item in cases.OrderBy(static value => value.CaseId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("type", "object");
                writer.WritePropertyName("properties");
                writer.WriteStartObject();
                writer.WritePropertyName("caseId");
                writer.WriteStartObject();
                writer.WriteString("const", item.CaseId);
                writer.WriteEndObject();
                writer.WritePropertyName("payload");
                using (var payload = JsonDocument.Parse(item.PayloadSchemaJson))
                {
                    payload.RootElement.WriteTo(writer);
                }

                writer.WriteEndObject();
                writer.WritePropertyName("required");
                writer.WriteStartArray();
                writer.WriteStringValue("caseId");
                writer.WriteStringValue("payload");
                writer.WriteEndArray();
                writer.WriteBoolean("additionalProperties", false);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteJsonType(Utf8JsonWriter writer, ITypeSymbol type)
    {
        writer.WriteStartObject();
        writer.WriteString("type", JsonTypeName(type));
        writer.WriteEndObject();
    }

    private static string JsonTypeName(ITypeSymbol type)
        => type.SpecialType switch
        {
            SpecialType.System_String => "string",
            SpecialType.System_Boolean => "boolean",
            SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16
                or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64 => "integer",
            SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal => "number",
            _ => "object",
        };

    private sealed record UnionDeclarationShape(
        string Name,
        IReadOnlyList<TypeSyntax> Cases,
        bool HasDefaultOrNullCase);
}

internal sealed record InputContractLoweringResult(
    bool Succeeded,
    string Diagnostics,
    BehaviorContractManifest? Contract,
    string? UnionName,
    IReadOnlyList<string> CaseIds,
    BehaviorCompilerPolicy Policy,
    string LoweringEvidenceJson)
{
    public static InputContractLoweringResult Ok(
        BehaviorContractManifest contract,
        string unionName,
        IReadOnlyList<string> caseIds,
        BehaviorCompilerPolicy policy)
        => new(
            true,
            string.Empty,
            contract,
            unionName,
            caseIds,
            policy,
            JsonSerializer.Serialize(new
            {
                succeeded = true,
                unionName,
                caseIds,
                behaviorContractId = contract.BehaviorContractId,
                contractMajorVersion = contract.ContractMajorVersion,
                caseCount = caseIds.Count,
                policy = policy.PolicyId,
                sdk = policy.SdkVersion,
                roslyn = policy.RoslynVersion,
                languageVersion = policy.LanguageVersion,
            }));

    public static InputContractLoweringResult Fail(string diagnostics, BehaviorCompilerPolicy policy)
        => new(
            false,
            diagnostics,
            null,
            null,
            [],
            policy,
            JsonSerializer.Serialize(new
            {
                succeeded = false,
                detail = diagnostics,
                policy = policy.PolicyId,
                sdk = policy.SdkVersion,
                roslyn = policy.RoslynVersion,
                languageVersion = policy.LanguageVersion,
            }));
}
