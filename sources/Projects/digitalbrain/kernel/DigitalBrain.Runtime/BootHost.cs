using DigitalBrain.InoLang;
using DigitalBrain.InoLang.Linking;
using DigitalBrain.InoLang.Planning;
using DigitalBrain.InoLang.Runtime;

namespace DigitalBrain.Runtime;

public sealed record BootOutcome(int ExitCode, ActivationResult? Result, string Message);

// Cold-start path: read the Genesis .ino, compile it, enforce L6 at power-on
// (refuse to run if its scenario is empty or red), then fire the boot handler
// with the boot-mode neuron bound. There is no cortex yet at cold start, so a
// failure here is a clean non-zero exit, not a failure synapse (self-hosting
// boot design §7).
public static class BootHost
{
    public const string GenesisInboundPort = "loaded";
    public const int ExitOk = 0;
    public const int ExitCompileError = 1;
    public const int ExitGateRefused = 2;
    public const int ExitBootFault = 3;

    public static string GenesisSource { get; } = LoadEmbeddedGenesis();

    public static async Task<BootOutcome> RunAsync(
        string inoSource,
        IReadOnlyDictionary<string, string> bootArgs,
        INeuronHost neuron,
        IContractCatalog catalog,
        CancellationToken ct)
    {
        var compiled = InoCompiler.Compile(inoSource, catalog);
        if (!compiled.Success)
            return new BootOutcome(ExitCompileError, null,
                "genesis did not compile: " + string.Join("; ",
                    compiled.Diagnostics.Select(d => $"{d.Code} {d.Message}")));

        var gate = await compiled.EvaluateGateAsync(ct);
        if (!gate.CanActivate)
            return new BootOutcome(ExitGateRefused, null, gate.Reason);

        try
        {
            var result = await new Interpreter(compiled.Plan!).RunAsync(
                TriggerKey.Port(GenesisInboundPort), bootArgs, neuron, ct);
            return new BootOutcome(ExitOk, result,
                $"{compiled.Plan!.Fqn} booted — all scenarios green.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new BootOutcome(ExitBootFault, null,
                "boot neuron fault: " + ex.Message);
        }
    }

    public static Task<BootOutcome> RunFromFileAsync(
        string inoPath,
        IReadOnlyDictionary<string, string> bootArgs,
        INeuronHost neuron,
        CancellationToken ct)
        => RunAsync(File.ReadAllText(inoPath), bootArgs, neuron,
            BootstrapCatalog.Default, ct);

    static string LoadEmbeddedGenesis()
    {
        using var stream = typeof(BootHost).Assembly
            .GetManifestResourceStream("DigitalBrain.Genesis.ino")
            ?? throw new InvalidOperationException(
                "Embedded Genesis artifact 'DigitalBrain.Genesis.ino' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
