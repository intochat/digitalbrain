using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Capabilities.Secrets;

/// <summary>
/// A secrets@1 failure with a stable <see cref="Code"/>. The message is <c>secret &lt;code&gt;: &lt;name&gt;</c> and
/// never contains a value.
/// </summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Contract errors carry a name and a code only.")]
public abstract class SecretException : Exception
{
    /// <summary>Creates the error for the secret <paramref name="name"/>.</summary>
    protected SecretException(string code, string name, Exception? innerException)
        : base($"secret {code}: {Validate(name)}", innerException)
    {
        Code = code;
        Name = name;
    }

    /// <summary>The stable error code.</summary>
    public string Code { get; }

    /// <summary>The secret's name.</summary>
    public string Name { get; }

    private static string Validate(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }
}

/// <summary><c>not-found</c>: the reference is unset or the secret is gone.</summary>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Contract error names.")]
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Contract errors carry a name and a code only.")]
public sealed class SecretNotFound(string name, Exception? innerException = null) : SecretException("not-found", name, innerException);

/// <summary><c>denied</c>: the runtime caller may not touch the secret.</summary>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Contract error names.")]
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Contract errors carry a name and a code only.")]
public sealed class SecretDenied(string name, Exception? innerException = null) : SecretException("denied", name, innerException);
