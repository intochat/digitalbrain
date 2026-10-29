namespace DigitalBrain.Capabilities.Identity;

/// <summary>identity@1, description: what the connected identity implementation declares about itself.</summary>
[Capability("identity", 1)]
public interface IIdentityProvider
{
    /// <summary>The descriptor; constant for the life of the connection.</summary>
    IdentityProviderDescriptor Describe();
}

/// <summary>Whether an identity implementation authenticates callers.</summary>
public enum IdentityPosture
{
    /// <summary>Anyone reaching the install is the local owner (a local install). An auth module refuses to start on it.</summary>
    Open = 0,

    /// <summary>Callers sign in and carry a session.</summary>
    Secured = 1,
}

/// <summary>The descriptor of a connected identity implementation.</summary>
public sealed record IdentityProviderDescriptor
{
    /// <summary>The implementation's id, for example <c>auth</c>.</summary>
    public required string ProviderId { get; init; }

    /// <summary>Open or Secured.</summary>
    public IdentityPosture Posture { get; init; }

    /// <summary>The neuron that renders the signed-in member's account (Settings, Account tab); null hides the tab.</summary>
    public NeuronId? AccountSurface { get; init; }
}
