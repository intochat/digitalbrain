using System.Collections.Concurrent;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Capabilities.Secrets;
using DigitalBrain.Testing.Conformance;
using DigitalBrain.Testing.Conformance.Identity;
using DigitalBrain.Testing.Conformance.Secrets;

namespace DigitalBrain.Testing;

/// <summary>
/// A secrets@1 fake for <see cref="TestOS"/> and neuron tests: the <see cref="InMemorySecrets"/> reference implementation
/// (it passes the secrets conformance kit) plus a record of the <see cref="ISecrets"/> members called.
/// </summary>
/// <param name="callers">Supplies the caller; a local owner (brain <c>owner</c>) when null.</param>
public sealed class FakeSecrets(AmbientCallerScope? callers = null) : InMemorySecrets(callers)
{
    private readonly ConcurrentQueue<string> _calls = new();

    /// <summary>The <see cref="ISecrets"/> members called, in order.</summary>
    public IReadOnlyList<string> Calls => [.. _calls];

    /// <inheritdoc />
    protected override void OnCall(string member) => _calls.Enqueue(member);
}

/// <summary>
/// An identity@1 fake for <see cref="TestOS"/> and neuron tests: the <see cref="InMemoryIdentity"/> reference implementation
/// (it passes the identity conformance kit) plus a record of the identity@1 members called.
/// </summary>
/// <param name="callers">Supplies the caller; a local owner when null.</param>
/// <param name="time">The clock for created, joined and granted times.</param>
public sealed class FakeIdentity(AmbientCallerScope? callers = null, TimeProvider? time = null) : InMemoryIdentity(callers, time)
{
    private readonly ConcurrentQueue<string> _calls = new();

    /// <summary>The <see cref="IIdentity"/> and <see cref="IGrants"/> members called, in order.</summary>
    public IReadOnlyList<string> Calls => [.. _calls];

    /// <inheritdoc />
    protected override void OnCall(string member) => _calls.Enqueue(member);
}

/// <summary>The secrets@1 and identity@1 helpers over <see cref="TestOS.With{TCapability}(string, TCapability)"/>.</summary>
public static class TestOSSecretsIdentityExtensions
{
    /// <summary>Binds <paramref name="secrets"/> as the secrets@1 connection named <paramref name="name"/>.</summary>
    public static TestOS WithSecrets(this TestOS os, string name, ISecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(os);
        return os.With(name, secrets);
    }

    /// <summary>Binds a new <see cref="FakeSecrets"/> as the secrets@1 connection named <paramref name="name"/>.</summary>
    /// <param name="os">The TestOS.</param>
    /// <param name="name">The connection name.</param>
    /// <param name="configure">Seeds (<see cref="InMemorySecrets.Put"/>) or captures the fake before binding.</param>
    public static TestOS WithSecrets(this TestOS os, string name, Action<FakeSecrets> configure)
    {
        ArgumentNullException.ThrowIfNull(os);
        var fake = new FakeSecrets();
        ArgumentNullException.ThrowIfNull(configure);
        configure(fake);
        return os.With<ISecrets>(name, fake);
    }

    /// <summary>Binds a new, empty <see cref="FakeSecrets"/> as the secrets@1 connection named <paramref name="name"/>.</summary>
    public static TestOS WithSecrets(this TestOS os, string name) => os.WithSecrets(name, static _ => { });

    /// <summary>Binds both identity@1 halves as the connection named <paramref name="name"/>.</summary>
    public static TestOS WithIdentity(this TestOS os, string name, IIdentity identity, IGrants grants)
    {
        ArgumentNullException.ThrowIfNull(os);
        return os.With(name, identity).With(name, grants);
    }

    /// <summary>
    /// Binds a new <see cref="FakeIdentity"/> (on the TestOS clock) as the identity@1 connection named <paramref name="name"/>:
    /// <see cref="IIdentity"/>, <see cref="IGrants"/>, <see cref="ISessions"/> and <see cref="IIdentityProvider"/>.
    /// </summary>
    /// <param name="os">The TestOS.</param>
    /// <param name="name">The connection name.</param>
    /// <param name="configure">Seeds or captures the fake before binding.</param>
    public static TestOS WithIdentity(this TestOS os, string name, Action<FakeIdentity> configure)
    {
        ArgumentNullException.ThrowIfNull(os);
        var fake = new FakeIdentity(time: os.Clock);
        ArgumentNullException.ThrowIfNull(configure);
        configure(fake);
        return os.WithIdentity(name, fake, fake).With<ISessions>(name, fake).With<IIdentityProvider>(name, fake);
    }

    /// <summary>Binds a new, empty <see cref="FakeIdentity"/> as both identity@1 halves named <paramref name="name"/>.</summary>
    public static TestOS WithIdentity(this TestOS os, string name) => os.WithIdentity(name, static _ => { });
}
