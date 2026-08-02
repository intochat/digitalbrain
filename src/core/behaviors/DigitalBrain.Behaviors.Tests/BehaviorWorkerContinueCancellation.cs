using System.Text.RegularExpressions;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorWorkerContinueCancellation
{
    [Fact(DisplayName =
        "BehaviorWorkerNeuron.Continue must pass a cancelable turn/attempt token into ReadValidatedTask (not CancellationToken.None)")]
    public void ContinuePassesCancelableTokenIntoReadValidatedTask()
    {
        // Source guard remains until the runtime outbox→Deliver path supplies a real cancelable token.
        // Runtime proof lives in Tasks.Tests: OutboxDeliveredDispatchWorkerContinueReceivesCancelableToken.
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "core",
            "behaviors",
            "DigitalBrain.Behaviors.Runtime",
            "BehaviorWorkerNeuron.cs");
        Assert.True(File.Exists(sourcePath), $"Missing BehaviorWorkerNeuron at {sourcePath}");

        var source = File.ReadAllText(sourcePath);
        // Explicit handler-token overload: DispatchWorkerContinue must pass CT into Continue/ReadValidatedTask.
        var continueMatch = Regex.Match(
            source,
            @"public\s+async\s+Task\s+Continue\s*\(\s*AttemptCursor\s+cursor\s*,\s*CancellationToken\s+cancellationToken\s*\)\s*\{(?<body>.*?)(?=\n\s{4}public\s+)",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(
            continueMatch.Success,
            "Could not locate BehaviorWorkerNeuron.Continue(AttemptCursor, CancellationToken) method body.");

        var body = continueMatch.Groups["body"].Value;
        Assert.Contains("ReadValidatedTask", body, StringComparison.Ordinal);
        Assert.Contains("cancellationToken", body, StringComparison.Ordinal);

        Assert.DoesNotContain(
            "CancellationToken.None",
            body,
            StringComparison.Ordinal);

        // HandleAsync must pass the handler token into Continue, not only rely on TurnCancellationToken field.
        Assert.Contains(
            "Continue(command.Cursor, cancellationToken)",
            source,
            StringComparison.Ordinal);
    }

    [Fact(DisplayName =
        "Production durable outbox Deliver call sites must pass a real CancellationToken (not the parameterless/default overload)")]
    public void DurableOutboxDeliverCallSitesMustPassCancellationToken()
    {
        var root = FindRepositoryRoot();
        var outboxPath = Path.Combine(root, "src", "core", "kernel", "DigitalBrain.Kernel", "Neuron", "Neuron.Outbox.cs");
        Assert.True(File.Exists(outboxPath), $"Missing Neuron.Outbox at {outboxPath}");
        var outbox = File.ReadAllText(outboxPath);

        // Durable drain path must not call the parameterless/default Deliver overload.
        Assert.DoesNotContain(
            "await Deliver(entry.Delivery);",
            outbox,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "await GrainFactory.GetGrain<INeuron>(receiver.ToGrainId()).Deliver(entry.Delivery);",
            outbox,
            StringComparison.Ordinal);

        var tryDeliverMatch = Regex.Match(
            outbox,
            @"private\s+async\s+Task<bool>\s+TryDeliverAsync\s*\((?<args>[^)]*)\)\s*\{(?<body>.*?)(?=\n\s{4}private\s+|\n\s{4}public\s+|\n\})",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(tryDeliverMatch.Success, "Could not locate TryDeliverAsync body.");
        var args = tryDeliverMatch.Groups["args"].Value;
        var body = tryDeliverMatch.Groups["body"].Value;
        Assert.Contains("CancellationToken", args, StringComparison.Ordinal);
        Assert.Contains("Deliver(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("CancellationToken.None", body, StringComparison.Ordinal);
        // Deliver must receive the drain/lifecycle token, not the parameterless overload.
        Assert.Matches(
            new Regex(@"\.Deliver\(\s*entry\.Delivery\s*,\s*\w+\s*\)", RegexOptions.CultureInvariant),
            body);

        // IOutboxDrain.Drain must not hard-code None into the durable drain (retry may use a
        // bounded source; cleanup/retraction may remain noncancelable elsewhere).
        Assert.DoesNotContain(
            "DrainAsync(CancellationToken.None)",
            outbox,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find DigitalBrain.slnx above {AppContext.BaseDirectory}.");
    }
}
