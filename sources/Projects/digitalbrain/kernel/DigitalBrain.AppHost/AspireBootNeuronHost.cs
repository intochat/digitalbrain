using DigitalBrain.InoLang.Runtime;
using DigitalBrain.Runtime.Aspire;

namespace DigitalBrain.Hosting;

// Boot-mode binding of the InoLang $aspire neuron to the native Aspire
// connector: in-process, no bus yet (self-hosting-boot design §4, Boot mode).
// Maps the Genesis neuron's `ask $aspire to "..."` prompts onto the connector
// ABI. A connector fault surfaces as a thrown exception — at cold start there
// is no cortex to emit a failure synapse across, so BootHost turns it into a
// clean non-zero exit (design §7).
public sealed class AspireBootNeuronHost(IAspireBootConnector connector, string profile)
    : INeuronHost
{
    public const string SpawnClusterPrompt = "spawn cluster";
    public const string InstallDomainPrefix = "install domain ";

    public Task<string> AskAsync(string port, string prompt, CancellationToken ct)
    {
        if (port == "brains")
        {
            if (prompt == "list")
                return Task.FromResult("");
            if (prompt == "create primary")
                return Task.FromResult("ok");
        }

        if (prompt == SpawnClusterPrompt)
            return connector.SpawnClusterAsync(profile, ct);

        if (prompt.StartsWith(InstallDomainPrefix, StringComparison.Ordinal))
            return connector.InstallDomainAsync(
                prompt[InstallDomainPrefix.Length..], ct);

        if (prompt.StartsWith("restart resource ", StringComparison.Ordinal))
            return connector.RestartResourceAsync(prompt["restart resource ".Length..], ct);

        if (prompt.StartsWith("spin up resource ", StringComparison.Ordinal))
            return connector.StartResourceAsync(prompt["spin up resource ".Length..], ct);

        if (prompt.StartsWith("stop resource ", StringComparison.Ordinal))
            return connector.StopResourceAsync(prompt["stop resource ".Length..], ct);

        if (prompt == "reload assemblies")
            return connector.RestartResourceAsync("kernel", ct);

        throw new InvalidOperationException(
            $"Genesis asked $aspire an unrecognized prompt: '{prompt}'.");
    }

    // Genesis has no fuzzy predicates; boot returns deterministic false.
    public Task<bool> EvaluatePredicateAsync(
        string builtin, string subject, string target, CancellationToken ct)
        => Task.FromResult(false);
}
