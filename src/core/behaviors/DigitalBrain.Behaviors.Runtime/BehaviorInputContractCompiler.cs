using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Manifest;
using DigitalBrain.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DigitalBrain.Behaviors.Runtime;

internal static class BehaviorInputContractCompiler
{
    internal const string PolicyId = "contract-only-v1";
    internal const string SdkVersion = "11.0.100-preview.6";
    internal const string RoslynVersion = "5.6.0";
    internal const string LanguageVersionName = "Preview";
    private const string BehaviorBrainMetadataName = "DigitalBrain.Behaviors.BehaviorBrain`1";
    private const string BehaviorNeuronReferenceMetadataName = "DigitalBrain.Behaviors.BehaviorNeuronReference`1";
    private const string RequestSynapseMetadataName = "DigitalBrain.Abstractions.RequestSynapse`1";
    private const string AliasAttributeMetadataName = "Orleans.AliasAttribute";
    private const string BehaviorContextMetadataName = "DigitalBrain.Behaviors.IBehaviorContext";
    private const string DefaultInstanceName = "default";
    private const string DefaultInstancePolicy = "default";
    private const string NamedInstancePolicy = "named";

    internal static BehaviorCompilerPolicy DefaultPolicy { get; } = new(
        SdkVersion,
        RoslynVersion,
        LanguageVersionName,
        PolicyId);

    public static CapabilityGrantDerivationResult DeriveCapabilityGrants(
        string programSource,
        ImmutableArray<MetadataReference> references,
        ActiveCapabilityCatalog? catalog = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programSource);

        var tree = CSharpSyntaxTree.ParseText(
            programSource,
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "Behavior.cs",
            encoding: Encoding.UTF8);

        var compilation = CSharpCompilation.Create(
            assemblyName: "Behavior.CapabilityGrants",
            syntaxTrees: [tree],
            references: references,
            options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    deterministic: true,
                    nullableContextOptions: NullableContextOptions.Enable)
                .WithMetadataImportOptions(MetadataImportOptions.All));

        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetCompilationUnitRoot();
        var brainDefinition = compilation.GetTypeByMetadataName(BehaviorBrainMetadataName);
        var neuronReferenceDefinition = compilation.GetTypeByMetadataName(BehaviorNeuronReferenceMetadataName);
        var requestSynapseDefinition = compilation.GetTypeByMetadataName(RequestSynapseMetadataName);
        var aliasAttribute = compilation.GetTypeByMetadataName(AliasAttributeMetadataName);
        if (brainDefinition is null || neuronReferenceDefinition is null || requestSynapseDefinition is null)
        {
            return CapabilityGrantDerivationResult.Fail(
                "BehaviorBrain capability grant derivation requires BehaviorBrain and RequestSynapse metadata.");
        }

        var grants = new List<BehaviorCapabilityGrant>();
        var errors = new List<string>();

        foreach (var node in root.DescendantNodes())
        {
            if (model.GetOperation(node) is not IInvocationOperation invocation)
            {
                continue;
            }

            if (!IsBehaviorNeuronSendAsync(invocation.TargetMethod, neuronReferenceDefinition))
            {
                if (LooksLikeCapabilitySendLookalike(invocation, brainDefinition, neuronReferenceDefinition))
                {
                    errors.Add(
                        "Directed capability edges must be derived from BehaviorBrain.Get and BehaviorNeuronReference.SendAsync; non-BehaviorBrain lookalikes are rejected.");
                }

                continue;
            }

            if (!TryResolveGetPairing(
                    invocation,
                    model,
                    brainDefinition,
                    neuronReferenceDefinition,
                    out var neuronType,
                    out var instanceName,
                    out var pairingError))
            {
                errors.Add(pairingError ?? "Unresolvable BehaviorBrain.Get / SendAsync pairing.");
                continue;
            }

            if (!TryBuildGrant(
                    invocation,
                    neuronType,
                    instanceName,
                    requestSynapseDefinition,
                    aliasAttribute,
                    catalog,
                    out var grant,
                    out var grantError))
            {
                errors.Add(grantError ?? "Unable to derive directed capability grant.");
                continue;
            }

            grants.Add(grant);
        }

        if (errors.Count > 0)
        {
            return CapabilityGrantDerivationResult.Fail(string.Join(Environment.NewLine, errors.Distinct(StringComparer.Ordinal)));
        }

        var ordered = grants
            .Distinct()
            .OrderBy(static grant => grant.TargetNeuronContractId, StringComparer.Ordinal)
            .ThenBy(static grant => grant.AcceptedRequestSynapseId, StringComparer.Ordinal)
            .ThenBy(static grant => grant.AcceptedRequestSchemaVersion)
            .ThenBy(static grant => grant.EmittedResultSynapseId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static grant => grant.EmittedResultSchemaVersion ?? 0)
            .ThenBy(static grant => grant.TargetInstancePolicy, StringComparer.Ordinal)
            .ThenBy(static grant => grant.TargetInstanceName, StringComparer.Ordinal)
            .ToArray();

        return CapabilityGrantDerivationResult.Ok(ordered);
    }

    public static EventAliasDerivationResult DeriveEventAliases(
        string programSource,
        ImmutableArray<MetadataReference> references,
        ActiveCapabilityCatalog? catalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programSource);

        var tree = CSharpSyntaxTree.ParseText(
            programSource,
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "Behavior.cs",
            encoding: Encoding.UTF8);

        var compilation = CSharpCompilation.Create(
            assemblyName: "Behavior.EventAliases",
            syntaxTrees: [tree],
            references: references,
            options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    deterministic: true,
                    nullableContextOptions: NullableContextOptions.Enable)
                .WithMetadataImportOptions(MetadataImportOptions.All));

        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetCompilationUnitRoot();
        var aliasAttribute = compilation.GetTypeByMetadataName(AliasAttributeMetadataName);
        var triggers = FindDistinctProgramTriggers(root, model, compilation);

        if (triggers.Length != 1 || !TryContractId(triggers[0], aliasAttribute, out var alias))
        {
            return EventAliasDerivationResult.Ok([]);
        }

        // An alias is contract identity first: it grants a subscription only when the active
        // catalog says some module actually broadcasts that fact.
        var broadcast = catalog is not null
            && catalog.Modules
                .SelectMany(static module => module.Neurons)
                .SelectMany(static neuron => neuron.Emitted)
                .Any(synapse => string.Equals(synapse.ContractId, alias, StringComparison.Ordinal));

        return EventAliasDerivationResult.Ok(broadcast ? [alias] : []);
    }

    public static EventAliasDerivationResult DeriveBroadcastEmitAliases(
        string programSource,
        ImmutableArray<MetadataReference> references,
        ActiveCapabilityCatalog? catalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programSource);

        var tree = CSharpSyntaxTree.ParseText(
            programSource,
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "Behavior.cs",
            encoding: Encoding.UTF8);

        var compilation = CSharpCompilation.Create(
            assemblyName: "Behavior.EmitAliases",
            syntaxTrees: [tree],
            references: references,
            options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    deterministic: true,
                    nullableContextOptions: NullableContextOptions.Enable)
                .WithMetadataImportOptions(MetadataImportOptions.All));

        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetCompilationUnitRoot();
        var contextDefinition = compilation.GetTypeByMetadataName(BehaviorContextMetadataName);
        var aliasAttribute = compilation.GetTypeByMetadataName(AliasAttributeMetadataName);
        if (contextDefinition is null)
        {
            return EventAliasDerivationResult.Ok([]);
        }

        var aliases = new SortedSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();

        foreach (var node in root.DescendantNodes())
        {
            if (model.GetOperation(node) is not IInvocationOperation invocation
                || !string.Equals(invocation.TargetMethod?.Name, "EmitAsync", StringComparison.Ordinal)
                || !SymbolEqualityComparer.Default.Equals(
                    invocation.TargetMethod?.ContainingType?.OriginalDefinition,
                    contextDefinition))
            {
                continue;
            }

            if (invocation.Arguments.Length < 1
                || !TryResolveNamedType(invocation.Arguments[0].Value, out var factType)
                || factType is null
                || !TryContractId(factType, aliasAttribute, out var alias))
            {
                errors.Add(
                    "IBehaviorContext.EmitAsync requires a resolvable fact type carrying a stable Orleans Alias.");
                continue;
            }

            if (catalog is null
                || !catalog.Modules
                    .SelectMany(static module => module.Neurons)
                    .SelectMany(static neuron => neuron.Emitted)
                    .Any(synapse => string.Equals(synapse.ContractId, alias, StringComparison.Ordinal)))
            {
                errors.Add($"Behavior emits '{alias}', which no active module declares as a broadcast fact.");
                continue;
            }

            _ = aliases.Add(alias);
        }

        return errors.Count > 0
            ? EventAliasDerivationResult.Fail(string.Join(Environment.NewLine, errors.Distinct(StringComparer.Ordinal)))
            : EventAliasDerivationResult.Ok([.. aliases]);
    }

    private static bool IsBehaviorNeuronSendAsync(IMethodSymbol? method, INamedTypeSymbol neuronReferenceDefinition)
    {
        if (method is null
            || !string.Equals(method.Name, "SendAsync", StringComparison.Ordinal)
            || method.ContainingType is not INamedTypeSymbol containing
            || !containing.IsGenericType)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(containing.OriginalDefinition, neuronReferenceDefinition);
    }

    private static bool LooksLikeCapabilitySendLookalike(
        IInvocationOperation invocation,
        INamedTypeSymbol brainDefinition,
        INamedTypeSymbol neuronReferenceDefinition)
    {
        var method = invocation.TargetMethod;
        if (method is null || !string.Equals(method.Name, "SendAsync", StringComparison.Ordinal))
        {
            return false;
        }

        if (IsBehaviorNeuronSendAsync(method, neuronReferenceDefinition))
        {
            return false;
        }

        // A Get-like call whose containing type is not BehaviorBrain is a lookalike edge attempt.
        if (invocation.Instance is ILocalReferenceOperation local
            && TryFindAssignedGet(local.Local, local.SemanticModel ?? invocation.SemanticModel, brainDefinition, out _, out _, out _))
        {
            return true;
        }

        if (invocation.Instance is IInvocationOperation receiverInvocation
            && string.Equals(receiverInvocation.TargetMethod?.Name, "Get", StringComparison.Ordinal)
            && receiverInvocation.TargetMethod?.ContainingType is INamedTypeSymbol containing
            && !(containing.IsGenericType
                && SymbolEqualityComparer.Default.Equals(containing.OriginalDefinition, brainDefinition)))
        {
            return true;
        }

        if (invocation.Instance is ILocalReferenceOperation
            || (invocation.Instance is IInvocationOperation chained
                && string.Equals(chained.TargetMethod?.Name, "Get", StringComparison.Ordinal)))
        {
            return true;
        }

        return false;
    }

    private static bool TryResolveGetPairing(
        IInvocationOperation sendInvocation,
        SemanticModel model,
        INamedTypeSymbol brainDefinition,
        INamedTypeSymbol neuronReferenceDefinition,
        out INamedTypeSymbol neuronType,
        out string instanceName,
        out string? error)
    {
        neuronType = null!;
        instanceName = DefaultInstanceName;
        error = null;

        var receiver = UnwrapConversion(sendInvocation.Instance);
        if (receiver is IInvocationOperation getInvocation)
        {
            return TryReadGetInvocation(getInvocation, brainDefinition, neuronReferenceDefinition, out neuronType, out instanceName, out error);
        }

        if (receiver is ILocalReferenceOperation localReference)
        {
            if (!TryFindAssignedGet(
                    localReference.Local,
                    localReference.SemanticModel ?? model,
                    brainDefinition,
                    out neuronType,
                    out instanceName,
                    out error))
            {
                return false;
            }

            if (sendInvocation.TargetMethod?.ContainingType is INamedTypeSymbol referenceType
                && referenceType.IsGenericType
                && referenceType.TypeArguments[0] is INamedTypeSymbol sendNeuron
                && !SymbolEqualityComparer.Default.Equals(sendNeuron.OriginalDefinition, neuronType.OriginalDefinition))
            {
                error = "SendAsync target neuron type does not match the BehaviorBrain.Get pairing.";
                return false;
            }

            return true;
        }

        error = "SendAsync must be paired with a resolvable BehaviorBrain.Get receiver.";
        return false;
    }

    private static bool TryFindAssignedGet(
        ILocalSymbol local,
        SemanticModel? model,
        INamedTypeSymbol brainDefinition,
        out INamedTypeSymbol neuronType,
        out string instanceName,
        out string? error)
    {
        neuronType = null!;
        instanceName = DefaultInstanceName;
        error = null;
        if (model is null)
        {
            error = "Semantic model is required to resolve BehaviorBrain.Get pairings.";
            return false;
        }

        var assignments = new List<IInvocationOperation>();
        foreach (var node in model.SyntaxTree.GetRoot().DescendantNodes())
        {
            var operation = model.GetOperation(node);
            switch (operation)
            {
                case IVariableDeclaratorOperation declarator
                    when SymbolEqualityComparer.Default.Equals(declarator.Symbol, local)
                         && UnwrapConversion(declarator.Initializer?.Value) is IInvocationOperation get:
                    assignments.Add(get);
                    break;
                case ISimpleAssignmentOperation assignment
                    when assignment.Target is ILocalReferenceOperation target
                         && SymbolEqualityComparer.Default.Equals(target.Local, local)
                         && UnwrapConversion(assignment.Value) is IInvocationOperation get:
                    assignments.Add(get);
                    break;
            }
        }

        if (assignments.Count == 0)
        {
            error = $"Local '{local.Name}' is not assigned from BehaviorBrain.Get.";
            return false;
        }

        if (assignments.Count > 1)
        {
            error = $"Local '{local.Name}' has ambiguous BehaviorBrain.Get assignments.";
            return false;
        }

        return TryReadGetInvocation(
            assignments[0],
            brainDefinition,
            neuronReferenceDefinition: null,
            out neuronType,
            out instanceName,
            out error);
    }

    private static bool TryReadGetInvocation(
        IInvocationOperation getInvocation,
        INamedTypeSymbol brainDefinition,
        INamedTypeSymbol? neuronReferenceDefinition,
        out INamedTypeSymbol neuronType,
        out string instanceName,
        out string? error)
    {
        neuronType = null!;
        instanceName = DefaultInstanceName;
        error = null;

        var method = getInvocation.TargetMethod;
        if (method is null
            || !string.Equals(method.Name, "Get", StringComparison.Ordinal)
            || method.ContainingType is not INamedTypeSymbol containing
            || !containing.IsGenericType
            || !SymbolEqualityComparer.Default.Equals(containing.OriginalDefinition, brainDefinition))
        {
            error = "Directed capability edges must use BehaviorBrain.Get; non-BehaviorBrain lookalikes are rejected.";
            return false;
        }

        if (method.TypeArguments.Length != 1 || method.TypeArguments[0] is not INamedTypeSymbol resolvedNeuron)
        {
            error = "BehaviorBrain.Get requires a concrete neuron contract type argument.";
            return false;
        }

        neuronType = resolvedNeuron.OriginalDefinition;
        if (getInvocation.Arguments.Length == 0)
        {
            instanceName = DefaultInstanceName;
            return true;
        }

        var nameArgument = getInvocation.Arguments[0].Value;
        if (!TryReadConstantString(nameArgument, out instanceName) || string.IsNullOrWhiteSpace(instanceName))
        {
            error = "BehaviorBrain.Get instance name must be a constant non-empty string.";
            return false;
        }

        _ = neuronReferenceDefinition;
        return true;
    }

    private static bool TryBuildGrant(
        IInvocationOperation sendInvocation,
        INamedTypeSymbol neuronType,
        string instanceName,
        INamedTypeSymbol requestSynapseDefinition,
        INamedTypeSymbol? aliasAttribute,
        ActiveCapabilityCatalog? catalog,
        out BehaviorCapabilityGrant grant,
        out string? error)
    {
        grant = null!;
        error = null;

        if (!TryContractId(neuronType, aliasAttribute, out var neuronContractId))
        {
            error = $"Neuron contract '{neuronType.Name}' requires a stable Orleans Alias for capability grants.";
            return false;
        }

        var method = sendInvocation.TargetMethod!;
        INamedTypeSymbol? requestType = null;
        INamedTypeSymbol? resultType = null;

        if (method.TypeParameters.Length == 1 && method.TypeArguments.Length == 1)
        {
            // Task<TResponse> SendAsync<TResponse>(RequestSynapse<TResponse> request, ...)
            if (sendInvocation.Arguments.Length < 1
                || !TryResolveRequestArgument(sendInvocation.Arguments[0].Value, requestSynapseDefinition, out requestType, out resultType)
                || resultType is null)
            {
                // Prefer the method type argument when the argument conversion is opaque.
                if (method.TypeArguments[0] is INamedTypeSymbol typedResult
                    && sendInvocation.Arguments.Length >= 1
                    && TryResolveNamedType(sendInvocation.Arguments[0].Value, out requestType)
                    && requestType is not null)
                {
                    resultType = typedResult.OriginalDefinition;
                }
                else
                {
                    error = "Typed SendAsync requires a resolvable RequestSynapse request and result type.";
                    return false;
                }
            }
        }
        else
        {
            // Task SendAsync(Synapse synapse, ...)
            if (sendInvocation.Arguments.Length < 1
                || !TryResolveNamedType(sendInvocation.Arguments[0].Value, out requestType)
                || requestType is null)
            {
                error = "One-way SendAsync requires a resolvable synapse request type.";
                return false;
            }

            resultType = null;
        }

        if (!TryContractId(requestType, aliasAttribute, out var requestContractId))
        {
            error = $"Request synapse '{requestType.Name}' requires a stable Orleans Alias for capability grants.";
            return false;
        }

        if (!TryResolveSynapseVersion(
                catalog,
                neuronContractId,
                requestContractId,
                accepted: true,
                out var requestVersion,
                out error))
        {
            return false;
        }

        string? resultContractId = null;
        int? resultVersion = null;
        if (resultType is not null)
        {
            if (!TryContractId(resultType, aliasAttribute, out resultContractId))
            {
                error = $"Result synapse '{resultType.Name}' requires a stable Orleans Alias for capability grants.";
                return false;
            }

            if (!TryResolveSynapseVersion(
                    catalog,
                    neuronContractId,
                    resultContractId,
                    accepted: false,
                    out var resolvedResultVersion,
                    out error))
            {
                return false;
            }

            resultVersion = resolvedResultVersion;
        }

        var policy = string.Equals(instanceName, DefaultInstanceName, StringComparison.Ordinal)
            ? DefaultInstancePolicy
            : NamedInstancePolicy;

        grant = new BehaviorCapabilityGrant(
            neuronContractId,
            requestContractId,
            requestVersion,
            resultContractId,
            resultVersion,
            policy,
            instanceName);
        return true;
    }

    private static bool TryResolveSynapseVersion(
        ActiveCapabilityCatalog? catalog,
        string neuronContractId,
        string synapseContractId,
        bool accepted,
        out int version,
        out string? error)
    {
        version = 0;
        error = null;

        if (catalog is null)
        {
            error = "Active capability catalog is required to resolve directed grant schema versions.";
            return false;
        }

        if (!catalog.TryGetNeuron(neuronContractId, out var neuron) || neuron is null)
        {
            error = $"Directed capability edge targets undeclared or inactive neuron '{neuronContractId}'.";
            return false;
        }

        var edges = accepted ? neuron.Accepted : neuron.Emitted;
        var matches = edges
            .Where(edge => string.Equals(edge.ContractId, synapseContractId, StringComparison.Ordinal))
            .ToArray();

        if (matches.Length == 0)
        {
            error = accepted
                ? $"Undeclared request synapse '{synapseContractId}' on neuron '{neuronContractId}'."
                : $"Incompatible result synapse '{synapseContractId}' for neuron '{neuronContractId}'.";
            return false;
        }

        if (matches.Length > 1)
        {
            error = $"Ambiguous schema versions for synapse '{synapseContractId}' on neuron '{neuronContractId}'.";
            return false;
        }

        version = matches[0].SchemaVersion;
        return true;
    }

    private static bool TryResolveRequestArgument(
        IOperation? argument,
        INamedTypeSymbol requestSynapseDefinition,
        out INamedTypeSymbol requestType,
        out INamedTypeSymbol? resultType)
    {
        requestType = null!;
        resultType = null;
        argument = UnwrapConversion(argument);
        if (!TryResolveNamedType(argument, out requestType) || requestType is null)
        {
            return false;
        }

        for (var current = requestType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType
                && SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, requestSynapseDefinition)
                && current.TypeArguments[0] is INamedTypeSymbol response)
            {
                resultType = response.OriginalDefinition;
                requestType = requestType.OriginalDefinition;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveNamedType(IOperation? operation, out INamedTypeSymbol type)
    {
        type = null!;
        operation = UnwrapConversion(operation);
        var candidate = operation?.Type as INamedTypeSymbol
            ?? (operation as IObjectCreationOperation)?.Type as INamedTypeSymbol
            ?? (operation as IConversionOperation)?.Type as INamedTypeSymbol
            ?? (operation as IInvocationOperation)?.Type as INamedTypeSymbol;
        if (candidate is null)
        {
            return false;
        }

        type = candidate.OriginalDefinition;
        return true;
    }

    private static bool TryReadConstantString(IOperation? operation, out string value)
    {
        value = DefaultInstanceName;
        operation = UnwrapConversion(operation);
        if (operation is null)
        {
            return true;
        }

        if (operation.ConstantValue is { HasValue: true, Value: string constant })
        {
            value = constant;
            return true;
        }

        if (operation is ILiteralOperation { ConstantValue.HasValue: true, ConstantValue.Value: string literal })
        {
            value = literal;
            return true;
        }

        if (operation is IDefaultValueOperation)
        {
            value = DefaultInstanceName;
            return true;
        }

        return false;
    }

    private static IOperation? UnwrapConversion(IOperation? operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private static bool TryContractId(
        INamedTypeSymbol type,
        INamedTypeSymbol? aliasAttribute,
        out string contractId)
    {
        contractId = string.Empty;
        if (aliasAttribute is null)
        {
            return false;
        }

        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass is null
                || !SymbolEqualityComparer.Default.Equals(
                    attribute.AttributeClass.OriginalDefinition,
                    aliasAttribute))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is string alias
                && alias.Length > 0)
            {
                contractId = alias;
                return true;
            }
        }

        return false;
    }

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

        if (unions.Length > 1)
        {
            var names = string.Join(", ", unions.Select(static item => item.Name).Order(StringComparer.Ordinal));
            return InputContractLoweringResult.Fail(
                $"A behavior cannot declare more than one root input union: {names}.",
                DefaultPolicy);
        }

        if (unions.Length == 1)
        {
            return LowerRootUnion(unions[0], root, model, behavior, resultSchemaJson);
        }

        return LowerSingleProgramTrigger(root, model, compilation, behavior, resultSchemaJson);
    }

    private static InputContractLoweringResult LowerRootUnion(
        UnionDeclarationShape union,
        CompilationUnitSyntax root,
        SemanticModel model,
        BehaviorId behavior,
        string? resultSchemaJson)
    {
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
            loweredCases.Add(CaseManifest(type));
        }

        return SucceedContract(behavior, union.Name, loweredCases, resultSchemaJson);
    }

    private static InputContractLoweringResult LowerSingleProgramTrigger(
        CompilationUnitSyntax root,
        SemanticModel model,
        CSharpCompilation compilation,
        BehaviorId behavior,
        string? resultSchemaJson)
    {
        var triggers = FindDistinctProgramTriggers(root, model, compilation);
        if (triggers.Length == 0)
        {
            return InputContractLoweringResult.Fail(
                "A behavior must declare exactly one logical input: a root input union or one public IBehaviorProgram trigger.",
                DefaultPolicy);
        }

        if (triggers.Length > 1)
        {
            var names = string.Join(", ", triggers.Select(static item => item.Name).Order(StringComparer.Ordinal));
            return InputContractLoweringResult.Fail(
                $"A behavior cannot declare more than one distinct program trigger: {names}.",
                DefaultPolicy);
        }

        var trigger = triggers[0];
        if (!IsImmutableRecordShape(trigger))
        {
            return InputContractLoweringResult.Fail(
                $"Program trigger '{trigger.Name}' must be an immutable record shape.",
                DefaultPolicy);
        }

        return SucceedContract(
            behavior,
            trigger.Name,
            new List<BehaviorContractCaseManifest> { CaseManifest(trigger) },
            resultSchemaJson);
    }


    private static INamedTypeSymbol[] FindDistinctProgramTriggers(
        CompilationUnitSyntax root,
        SemanticModel model,
        CSharpCompilation compilation)
    {
        var programDefinition = compilation.GetTypeByMetadataName("DigitalBrain.Behaviors.IBehaviorProgram`1");
        if (programDefinition is null)
        {
            return [];
        }

        var distinct = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        foreach (var declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol type
                || type.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            foreach (var face in type.AllInterfaces)
            {
                if (!face.IsGenericType
                    || !SymbolEqualityComparer.Default.Equals(face.OriginalDefinition, programDefinition)
                    || face.TypeArguments[0] is not INamedTypeSymbol trigger)
                {
                    continue;
                }

                trigger = trigger.OriginalDefinition;
                distinct[trigger.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)] = trigger;
            }
        }

        return distinct.Values
            .OrderBy(static item => item.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static BehaviorContractCaseManifest CaseManifest(INamedTypeSymbol type)
        => new(
            CaseId: $"case.{type.Name}",
            CaseSchemaVersion: 1,
            CaseName: type.Name,
            PayloadSchemaJson: PayloadSchemaJson(type));

    private static InputContractLoweringResult SucceedContract(
        BehaviorId behavior,
        string logicalInputName,
        List<BehaviorContractCaseManifest> cases,
        string? resultSchemaJson)
    {
        if (cases.Count == 0)
        {
            return InputContractLoweringResult.Fail(
                "A successful behavior input contract must contain at least one case.",
                DefaultPolicy);
        }

        var orderedCases = cases
            .OrderBy(static item => item.CaseId, StringComparer.Ordinal)
            .ToArray();
        var oneOf = OneOfSchemaJson(orderedCases);
        if (string.IsNullOrWhiteSpace(oneOf) || oneOf.Contains("\"oneOf\":[]", StringComparison.Ordinal))
        {
            return InputContractLoweringResult.Fail(
                "A successful behavior input contract cannot lower to an empty oneOf placeholder.",
                DefaultPolicy);
        }

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
            logicalInputName,
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

internal sealed record EventAliasDerivationResult(
    bool Succeeded,
    string Diagnostics,
    IReadOnlyList<string> EventAliases)
{
    public static EventAliasDerivationResult Ok(IReadOnlyList<string> eventAliases)
        => new(true, string.Empty, eventAliases);

    public static EventAliasDerivationResult Fail(string diagnostics)
        => new(false, diagnostics, Array.Empty<string>());
}

internal sealed record CapabilityGrantDerivationResult(
    bool Succeeded,
    string Diagnostics,
    IReadOnlyList<BehaviorCapabilityGrant> Grants)
{
    public static CapabilityGrantDerivationResult Ok(IReadOnlyList<BehaviorCapabilityGrant> grants)
        => new(true, string.Empty, grants);

    public static CapabilityGrantDerivationResult Fail(string diagnostics)
        => new(false, diagnostics, Array.Empty<BehaviorCapabilityGrant>());
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
