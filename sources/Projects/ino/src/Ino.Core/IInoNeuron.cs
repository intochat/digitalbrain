namespace Ino.Core;

public interface IInoNeuron : IGrainWithStringKey
{
    // Single entry point. Routes via ICortexCapability and returns a response.
    // Grain key is "{userId}/{sessionId}" — see InoNeuronGrainKey.Format.
    Task<InoResponse> AskAsync(string prompt, string correlationId, CancellationToken ct);
}

public static class InoNeuronGrainKey
{
    public const string DefaultSessionId = "default";
    public const string AutonomicSessionId = "autonomic";

    public static string Format(string userId, string sessionId) => $"{userId}/{sessionId}";

    public static (string UserId, string SessionId) Parse(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var slash = key.IndexOf('/');
        if (slash < 0)
            throw new ArgumentException(
                $"Grain key '{key}' is not in 'userId/sessionId' shape — produce keys via InoNeuronGrainKey.Format.",
                nameof(key));
        return (key[..slash], key[(slash + 1)..]);
    }
}
