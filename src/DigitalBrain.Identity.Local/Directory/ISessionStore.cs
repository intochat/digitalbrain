namespace DigitalBrain.Identity.Local.Directory;

using DigitalBrain.Brains;
using DigitalBrain.Capabilities.Identity.Model;
using DigitalBrain.Contracts;
using DigitalBrain.Neurons;

/// <summary>Neuron contract governing active sessions in the system brain.</summary>
[NeuronAlias(Type), DefaultNeuronType(Type)]
public interface ISessionStore
{
    /// <summary>The neuron type name.</summary>
    public const string Type = "identity.sessions";

    /// <summary>The session store neuron key in the system brain.</summary>
    public const string Key = "system/main";

    /// <summary>Builds the neuron identifier for the session store in a brain.</summary>
    public static NeuronId Id(string brainId = "system") => new(new BrainId(brainId), Type, "main");

    /// <summary>Starts an authenticated session.</summary>
    /// <param name="tokenHash">The SHA256 hex hash of the session token.</param>
    /// <param name="brainId">The brain identifier.</param>
    /// <param name="principalId">The principal identifier.</param>
    /// <param name="expiresAt">When the session expires.</param>
    /// <returns>The created session.</returns>
    Task<Session> Start(string tokenHash, string brainId, string principalId, DateTimeOffset expiresAt);

    /// <summary>Ends a session by its token hash.</summary>
    /// <param name="tokenHash">The token hash to remove.</param>
    Task End(string tokenHash);

    /// <summary>Finds an active unexpired session by its token hash.</summary>
    /// <param name="tokenHash">The token hash.</param>
    /// <returns>The session if found and not expired, otherwise null.</returns>
    Task<Session?> Find(string tokenHash);
}
