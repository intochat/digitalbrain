using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DigitalBrain.Runtime.Synapses;

/// <summary>
/// The host's boundary for a synapse build: a closed world. Every type and member reference in the PE bytes (nothing is loaded)
/// must be on <see cref="AllowedApis"/> or be a [Capability] interface (a slot contract), the compiler-only helpers may be called
/// only from &lt;PrivateImplementationDetails&gt;, and the build may declare no P/Invoke. DB0045 in the analyzer is advisory; this is the check.
/// </summary>
internal static class AllowedReferences
{
    /// <summary>The first reference outside the allowed surface, or null when every reference is allowed.</summary>
    /// <param name="assembly">The PE image of the build.</param>
    /// <returns>The fully qualified name of the first disallowed type or member.</returns>
    public static string? FirstDisallowed(byte[] assembly) => Disallowed(assembly).FirstOrDefault();

    /// <summary>Every reference outside the allowed surface, in metadata order.</summary>
    /// <param name="assembly">The PE image of the build.</param>
    /// <returns>The fully qualified names of the disallowed types and members.</returns>
    public static IEnumerable<string> Disallowed(byte[] assembly)
    {
        using var pe = new PEReader(ImmutableArray.Create(assembly));
        var metadata = pe.GetMetadataReader();

        // A P/Invoke (an ImplMap row) is native code whatever names it uses.
        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle);
            if (!method.GetImport().Name.IsNil || (method.Attributes & MethodAttributes.PinvokeImpl) != 0)
            {
                var owner = metadata.GetTypeDefinition(method.GetDeclaringType());
                yield return $"native import {metadata.GetString(owner.Name)}.{metadata.GetString(method.Name)}";
            }
        }

        // Explicit layout overlaps fields: two references of different types at one offset is type confusion. It shows only in
        // TypeDef flags and FieldLayout rows, never in a TypeRef.
        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            if ((type.Attributes & TypeAttributes.LayoutMask) == TypeAttributes.ExplicitLayout && !IsSizedBlob(metadata, type))
            {
                yield return $"explicit layout {metadata.GetString(type.Name)}";
            }
        }

        if (metadata.GetTableRowCount(TableIndex.FieldLayout) > 0)
        {
            yield return "explicit field offsets";
        }

        // One resolution per referenced type per deploy: the loaded-assembly scan is not repeated for each member.
        var loaded = AppDomain.CurrentDomain.GetAssemblies();
        var capabilities = new Dictionary<TypeReferenceHandle, bool>();
        bool IsCapability(TypeReferenceHandle reference)
        {
            if (!capabilities.TryGetValue(reference, out var capability))
            {
                capabilities[reference] = capability = IsCapabilityIn(loaded, metadata, reference);
            }

            return capability;
        }

        foreach (var handle in metadata.TypeReferences)
        {
            var (ns, type) = TypeName(metadata, handle);
            if (!AllowedApis.IsAllowedType(ns, type) && !IsCapability(handle))
            {
                yield return type;
            }
        }

        foreach (var handle in metadata.MemberReferences)
        {
            var reference = metadata.GetMemberReference(handle);
            if (Parent(metadata, reference.Parent) is not { } parent)
            {
                continue;
            }

            var member = metadata.GetString(reference.Name);
            var parameters = Parameters(metadata, reference);
            if (!AllowedApis.IsAllowedMember(parent.Namespace, parent.Type, member, string.Join(",", parameters)) && !IsCapability(parent.Handle))
            {
                yield return parent.Type + "." + member;
            }
            else if (parent.Namespace.StartsWith("System.Text.Json", StringComparison.Ordinal) && parameters.Contains("System.Type", StringComparer.Ordinal))
            {
                // A Type-taking serializer member constructs or reads any type the Type value names, outside the allow-list.
                yield return $"{parent.Type}.{member}({string.Join(",", parameters)})";
            }
        }

        foreach (var site in CompilerOnlyCallsOutsideHelpers(pe, metadata))
        {
            yield return site;
        }

        foreach (var named in AttributeTypeArguments(metadata))
        {
            yield return named;
        }
    }

    // The parameter types of a method reference (empty for a field), fully qualified with generic arity dropped.
    private static ImmutableArray<string> Parameters(MetadataReader metadata, MemberReference reference)
    {
        if (reference.GetKind() != MemberReferenceKind.Method)
        {
            return [];
        }

        try
        {
            return reference.DecodeMethodSignature(new SignatureNames(metadata), null).ParameterTypes;
        }
        catch (BadImageFormatException)
        {
            return ["<undecodable>"];
        }
    }

    // typeof(X) in an attribute argument is a serialized type name, never a TypeRef: a unit could read such an attribute back and hold
    // a Type for anything loaded. Every named type must be the build's own or on the allowed surface.
    private static List<string> AttributeTypeArguments(MetadataReader metadata)
    {
        var own = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in metadata.TypeDefinitions)
        {
            _ = own.Add(DefinitionName(metadata, handle));
        }

        var assemblyName = metadata.IsAssembly ? metadata.GetString(metadata.GetAssemblyDefinition().Name) : "";
        var decoder = new AttributeTypes(metadata);
        var refused = new List<string>();
        foreach (var handle in metadata.CustomAttributes)
        {
            var attribute = metadata.GetCustomAttribute(handle);
            CustomAttributeValue<string> value;
            try
            {
                value = attribute.DecodeValue(decoder);
            }
            catch (Exception error) when (error is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                refused.Add("undecodable custom attribute");
                continue;
            }

            var arguments = value.FixedArguments.Concat(value.NamedArguments.Select(a => new CustomAttributeTypedArgument<string>(a.Type, a.Value)));
            foreach (var serialized in arguments.SelectMany(TypeValues))
            {
                foreach (var (name, outer, assembly) in SerializedTypeNames.Parse(serialized))
                {
                    var isOwn = (assembly is null || string.Equals(assembly, assemblyName, StringComparison.Ordinal)) && own.Contains(name);
                    var dot = outer.LastIndexOf('.');
                    if (!isOwn && !AllowedApis.IsAllowedType(dot < 0 ? "" : outer[..dot], name))
                    {
                        refused.Add($"typeof({name}) in an attribute");
                    }
                }
            }
        }

        return refused;
    }

    private static IEnumerable<string> TypeValues(CustomAttributeTypedArgument<string> argument) => argument.Value switch
    {
        ImmutableArray<CustomAttributeTypedArgument<string>> items => items.SelectMany(TypeValues),
        string name when argument.Type == AttributeTypes.SystemType => [name],
        _ => [],
    };

    private static string DefinitionName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var type = metadata.GetTypeDefinition(handle);
        var name = metadata.GetString(type.Name);
        if (!type.GetDeclaringType().IsNil)
        {
            return DefinitionName(metadata, type.GetDeclaringType()) + "." + name;
        }

        var ns = metadata.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    // Signature types as the allow-list names them: fully qualified, nested types joined by '.', generic arity dropped.
    private sealed class SignatureNames(MetadataReader metadata) : ISignatureTypeProvider<string, object?>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Void => "System.Void",
            PrimitiveTypeCode.Boolean => "System.Boolean",
            PrimitiveTypeCode.Char => "System.Char",
            PrimitiveTypeCode.SByte => "System.SByte",
            PrimitiveTypeCode.Byte => "System.Byte",
            PrimitiveTypeCode.Int16 => "System.Int16",
            PrimitiveTypeCode.UInt16 => "System.UInt16",
            PrimitiveTypeCode.Int32 => "System.Int32",
            PrimitiveTypeCode.UInt32 => "System.UInt32",
            PrimitiveTypeCode.Int64 => "System.Int64",
            PrimitiveTypeCode.UInt64 => "System.UInt64",
            PrimitiveTypeCode.Single => "System.Single",
            PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.String => "System.String",
            PrimitiveTypeCode.IntPtr => "System.IntPtr",
            PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
            PrimitiveTypeCode.Object => "System.Object",
            PrimitiveTypeCode.TypedReference => "System.TypedReference",
            _ => typeCode.ToString(),
        };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => DefinitionName(metadata, handle);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => TypeName(metadata, handle).Type;

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            metadata.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";

        public string GetByReferenceType(string elementType) => elementType + "&";

        public string GetPointerType(string elementType) => elementType + "*";

        public string GetPinnedType(string elementType) => elementType;

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType;

        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

        public string GetFunctionPointerType(MethodSignature<string> signature) => "method*";

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    }

    // Decodes attribute blobs: System.Type arguments come back as their serialized names; enums are sized from their definitions.
    private sealed class AttributeTypes(MetadataReader metadata) : ICustomAttributeTypeProvider<string>
    {
        public const string SystemType = "System.Type";

        private readonly Dictionary<string, PrimitiveTypeCode> enums = new(StringComparer.Ordinal);

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetSystemType() => SystemType;

        public bool IsSystemType(string type) => type == SystemType;

        public string GetTypeFromSerializedName(string name) => name;

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var name = DefinitionName(metadata, handle);
            var type = metadata.GetTypeDefinition(handle);
            foreach (var fieldHandle in type.GetFields())
            {
                var field = metadata.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & FieldAttributes.Static) == 0
                    && Enum.TryParse<PrimitiveTypeCode>(field.DecodeSignature(new SignatureNames(metadata), null).Replace("System.", "", StringComparison.Ordinal), out var code))
                {
                    enums[name] = code;
                }
            }

            return name;
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var name = TypeName(metadata, handle).Type;
            if (!enums.ContainsKey(name))
            {
                var resolved = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType(name.Replace("+", ".", StringComparison.Ordinal), throwOnError: false) ?? NestedType(a, name))
                    .FirstOrDefault(t => t is { IsEnum: true });
                if (resolved is not null)
                {
                    enums[name] = Type.GetTypeCode(Enum.GetUnderlyingType(resolved)) switch
                    {
                        TypeCode.SByte => PrimitiveTypeCode.SByte,
                        TypeCode.Byte => PrimitiveTypeCode.Byte,
                        TypeCode.Int16 => PrimitiveTypeCode.Int16,
                        TypeCode.UInt16 => PrimitiveTypeCode.UInt16,
                        TypeCode.UInt32 => PrimitiveTypeCode.UInt32,
                        TypeCode.Int64 => PrimitiveTypeCode.Int64,
                        TypeCode.UInt64 => PrimitiveTypeCode.UInt64,
                        _ => PrimitiveTypeCode.Int32,
                    };
                }
            }

            return name;
        }

        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => enums.TryGetValue(type, out var code) ? code : PrimitiveTypeCode.Int32;

        // A nested reference is named Outer.Inner here; reflection names it Outer+Inner.
        private static Type? NestedType(Assembly assembly, string name)
        {
            var dot = name.LastIndexOf('.');
            return dot < 0 ? null : assembly.GetType(name[..dot] + "+" + name[(dot + 1)..], throwOnError: false);
        }
    }

    // A slot contract may live in any assembly the unit compiles against (a module's or a test's): the host resolves the
    // referenced type in the loaded assemblies and allows it when it is an interface marked [Capability].
    private static bool IsCapabilityIn(Assembly[] loaded, MetadataReader metadata, TypeReferenceHandle handle)
    {
        var reference = metadata.GetTypeReference(handle);
        if (reference.ResolutionScope.Kind != HandleKind.AssemblyReference)
        {
            return false;
        }

        var assemblyName = metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope).Name);
        var ns = metadata.GetString(reference.Namespace);
        var fullName = ns.Length == 0 ? metadata.GetString(reference.Name) : ns + "." + metadata.GetString(reference.Name);
        var type = loaded
            .Where(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.Ordinal))
            .Select(a => a.GetType(fullName, throwOnError: false))
            .FirstOrDefault(t => t is not null);
        return type is { IsInterface: true } && type.IsDefined(typeof(CapabilityAttribute), inherit: false);
    }

    private const string Helpers = "<PrivateImplementationDetails>";

    // The compiler sizes static array data with fieldless explicit-layout structs nested in <PrivateImplementationDetails>:
    // nothing overlaps without fields.
    private static bool IsSizedBlob(MetadataReader metadata, TypeDefinition type)
    {
        if (type.GetDeclaringType().IsNil || type.GetFields().Count > 0)
        {
            return false;
        }

        var owner = metadata.GetTypeDefinition(type.GetDeclaringType());
        return owner.GetDeclaringType().IsNil && owner.Namespace.IsNil && metadata.GetString(owner.Name) == Helpers;
    }

    private static readonly Dictionary<short, OperandType> Operands = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value, code => code.OperandType);

    // Unsafe.As/Add and MemoryMarshal.CreateSpan give raw memory access: the compiler's own helpers may call them, a unit may not.
    private static IEnumerable<string> CompilerOnlyCallsOutsideHelpers(PEReader pe, MetadataReader metadata)
    {
        foreach (var typeHandle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(typeHandle);
            var helpers = type.GetDeclaringType().IsNil && type.Namespace.IsNil && metadata.GetString(type.Name) == Helpers;
            if (helpers && !IsCompilerGenerated(metadata, type))
            {
                yield return Helpers + " not emitted by the compiler";
            }

            var stateMachine = IsStateMachine(metadata, type);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = metadata.GetMethodDefinition(methodHandle);
                if (helpers || method.RelativeVirtualAddress == 0)
                {
                    continue;
                }

                foreach (var token in MethodTokens(pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader()))
                {
                    if (CompilerOnlyName(metadata, token, stateMachine) is { } name)
                    {
                        yield return $"{name} called from {metadata.GetString(type.Name)}.{metadata.GetString(method.Name)}";
                    }
                }
            }
        }
    }

    private static bool IsCompilerGenerated(MetadataReader metadata, TypeDefinition type) =>
        type.GetCustomAttributes().Select(metadata.GetCustomAttribute).Any(attribute =>
            attribute.Constructor.Kind == HandleKind.MemberReference
            && metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent is { Kind: HandleKind.TypeReference } parent
            && TypeName(metadata, (TypeReferenceHandle)parent).Type == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");

    // A compiler-generated async state machine: a nested type named <Method>d__N (unspeakable in C#) implementing IAsyncStateMachine.
    private static bool IsStateMachine(MetadataReader metadata, TypeDefinition type)
    {
        var name = metadata.GetString(type.Name);
        return !type.GetDeclaringType().IsNil && name.StartsWith('<') && name.Contains(">d__", StringComparison.Ordinal)
            && type.GetInterfaceImplementations().Select(i => metadata.GetInterfaceImplementation(i).Interface)
                .Any(i => i.Kind == HandleKind.TypeReference && TypeName(metadata, (TypeReferenceHandle)i).Type == "System.Runtime.CompilerServices.IAsyncStateMachine");
    }

    private static string? CompilerOnlyName(MetadataReader metadata, EntityHandle token, bool inStateMachine)
    {
        if (token.Kind == HandleKind.MethodSpecification)
        {
            token = metadata.GetMethodSpecification((MethodSpecificationHandle)token).Method;
        }

        if (token.Kind != HandleKind.MemberReference)
        {
            return null;
        }

        var reference = metadata.GetMemberReference((MemberReferenceHandle)token);
        if (Parent(metadata, reference.Parent) is not { } parent)
        {
            return null;
        }

        var name = parent.Type + "." + metadata.GetString(reference.Name);
        return AllowedApis.CompilerOnly.Contains(name, StringComparer.Ordinal) || (!inStateMachine && AllowedApis.StateMachineOnly.Contains(name, StringComparer.Ordinal))
            ? name
            : null;
    }

    // Every method or token operand in the IL (call, callvirt, newobj, ldftn, ldvirtftn, jmp, ldtoken).
    private static List<EntityHandle> MethodTokens(BlobReader il)
    {
        var tokens = new List<EntityHandle>();
        while (il.RemainingBytes > 0)
        {
            short value = il.ReadByte();
            if (value == 0xFE)
            {
                value = unchecked((short)(0xFE00 | il.ReadByte()));
            }

            switch (Operands.TryGetValue(value, out var operand) ? operand : OperandType.InlineNone)
            {
                case OperandType.InlineMethod or OperandType.InlineTok:
                    tokens.Add(System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(il.ReadInt32()));
                    break;
                case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                    il.Offset += 1;
                    break;
                case OperandType.InlineVar:
                    il.Offset += 2;
                    break;
                case OperandType.InlineI8 or OperandType.InlineR:
                    il.Offset += 8;
                    break;
                case OperandType.InlineSwitch:
                    il.Offset += 4 * il.ReadInt32();
                    break;
                case OperandType.InlineNone:
                    break;
                default:
                    il.Offset += 4;
                    break;
            }
        }

        return tokens;
    }

    private static (string Namespace, string Type, TypeReferenceHandle Handle)? Parent(MetadataReader metadata, EntityHandle parent)
    {
        switch (parent.Kind)
        {
            case HandleKind.TypeReference:
                return WithHandle(metadata, (TypeReferenceHandle)parent);
            case HandleKind.TypeSpecification:
                // A generic instantiation: the blob starts GENERICINST, CLASS|VALUETYPE, then the open type's coded handle.
                var blob = metadata.GetBlobReader(metadata.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
                if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
                {
                    return null;
                }

                blob.ReadSignatureTypeCode();
                var open = blob.ReadTypeHandle();
                return open.Kind == HandleKind.TypeReference ? WithHandle(metadata, (TypeReferenceHandle)open) : null;
            default:
                // A TypeDef parent is the build's own type; a MethodDef parent is a vararg call into it.
                return null;
        }
    }

    private static (string Namespace, string Type, TypeReferenceHandle Handle) WithHandle(MetadataReader metadata, TypeReferenceHandle handle)
    {
        var (ns, type) = TypeName(metadata, handle);
        return (ns, type, handle);
    }

    // The outermost type's namespace and the full name with nested types joined by '.', generic arity dropped.
    private static (string Namespace, string Type) TypeName(MetadataReader metadata, TypeReferenceHandle handle)
    {
        var reference = metadata.GetTypeReference(handle);
        var name = metadata.GetString(reference.Name);
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
        {
            name = name[..tick];
        }

        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            var (ns, outer) = TypeName(metadata, (TypeReferenceHandle)reference.ResolutionScope);
            return (ns, outer + "." + name);
        }

        var space = metadata.GetString(reference.Namespace);
        return (space, space.Length == 0 ? name : space + "." + name);
    }
}
