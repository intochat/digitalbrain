namespace DigitalBrain.Abstractions;

public interface IModule
{
    ModuleDescriptor Descriptor { get; }
}

public sealed record ModuleDescriptor(
    string Id,
    string Version,
    string DisplayName,
    IReadOnlyList<ConfigurationKey> Configuration,
    IReadOnlyList<SecretRequirement> Secrets,
    IReadOnlyList<CapabilityDeclaration> Capabilities,
    IReadOnlyList<EffectDeclaration> Effects,
    IReadOnlyList<ConnectionDeclaration> Connections);

public sealed record ConfigurationKey(string Name, string Scope, string Description);

public sealed record SecretRequirement(string Name, string Description);

public sealed record CapabilityDeclaration(string Name, string Reason);

public sealed record EffectDeclaration(string Name, string Description);

public sealed record ConnectionDeclaration(string Provider, IReadOnlyList<string> Scopes);

public sealed class ModuleCompositionException : Exception
{
    public ModuleCompositionException()
    {
    }

    public ModuleCompositionException(string message) : base(message)
    {
    }

    public ModuleCompositionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
