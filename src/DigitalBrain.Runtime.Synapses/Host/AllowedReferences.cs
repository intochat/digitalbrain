using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
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

        foreach (var handle in metadata.TypeReferences)
        {
            var (ns, type) = TypeName(metadata, handle);
            if (!AllowedApis.IsAllowedType(ns, type) && !IsCapability(metadata, handle))
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
            if (!AllowedApis.IsAllowedMember(parent.Namespace, parent.Type, member) && !IsCapability(metadata, parent.Handle))
            {
                yield return parent.Type + "." + member;
            }
        }

        foreach (var site in CompilerOnlyCallsOutsideHelpers(pe, metadata))
        {
            yield return site;
        }
    }

    // A slot contract may live in any assembly the unit compiles against (a module's or a test's): the host resolves the
    // referenced type in the loaded assemblies and allows it when it is an interface marked [Capability].
    private static bool IsCapability(MetadataReader metadata, TypeReferenceHandle handle)
    {
        var reference = metadata.GetTypeReference(handle);
        if (reference.ResolutionScope.Kind != HandleKind.AssemblyReference)
        {
            return false;
        }

        var assemblyName = metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope).Name);
        var ns = metadata.GetString(reference.Namespace);
        var fullName = ns.Length == 0 ? metadata.GetString(reference.Name) : ns + "." + metadata.GetString(reference.Name);
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.Ordinal))
            .Select(a => a.GetType(fullName, throwOnError: false))
            .FirstOrDefault(t => t is not null);
        return type is { IsInterface: true } && type.IsDefined(typeof(CapabilityAttribute), inherit: false);
    }

    private const string Helpers = "<PrivateImplementationDetails>";

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

            foreach (var methodHandle in type.GetMethods())
            {
                var method = metadata.GetMethodDefinition(methodHandle);
                if (helpers || method.RelativeVirtualAddress == 0)
                {
                    continue;
                }

                foreach (var token in MethodTokens(pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader()))
                {
                    if (CompilerOnlyName(metadata, token) is { } name)
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

    private static string? CompilerOnlyName(MetadataReader metadata, EntityHandle token)
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
        return AllowedApis.CompilerOnly.Contains(name, StringComparer.Ordinal) ? name : null;
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
