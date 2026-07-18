using System.Text;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.InoLang;
using DigitalBrain.InoLang.Diagnostics;
using DigitalBrain.InoLang.Linking;
using DigitalBrain.InoLang.Testing;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel.Creator.InoAuthoring;

// E-SDK #57 sub-issue B. The InoLang-retargeted Creator loop.
//
// Flow:
//   1. Send (system = CreatorInoSystemPrompt, user = intent + suggested fqn)
//      to the keyed IChatClient.
//   2. Strip Markdown code fences from the response — the prompt asks for
//      raw .ino but real LLMs sometimes wrap anyway.
//   3. InoCompiler.Compile against the injected IContractCatalog. If
//      diagnostics carry errors → format them as ATTEMPT N feedback and
//      loop.
//   4. ScenarioRunner.RunAllAsync against the compiled plan. v3 §L6: a
//      neuron with zero scenarios or any red scenario is REFUSED — feed
//      the failures back as the next turn's context.
//   5. On green: persist via IInoNeuronStore and return the relative
//      path so the verification gate can call
//      InoScenarioProjection.RunAsync(root, relPath, name, "scenario:0",
//      catalog, ct) against the persisted file (the brief's index-
//      dispatch contract from #52).
//
// Why the loop owns the gate (rather than persist-then-gate via
// InoScenarioProjection.RunAsync): the brief's acceptance gate requires
// scenarios to be green BEFORE persistence — a red scenario must never
// reach the Generated/ subtree where DynamicGeneratedInoSource will
// pick it up after a later silo restart; the live authoring handlers
// register the returned registration immediately. Running ScenarioRunner directly here
// is the same code path RunAsync uses internally (InoCompiler.Compile
// + SharedRunner.RunAllAsync), so the in-loop verdict and the post-
// persistence gate cannot disagree.
public sealed class InoAuthoringLoop(
    IServiceProvider services,
    IContractCatalog catalog,
    IInoNeuronStore store,
    TimeProvider time,
    ILogger<InoAuthoringLoop> logger)
{
    static readonly ScenarioRunner SharedRunner = new();

    public async Task<InoAuthoringResult> AuthorAsync(
        InoAuthoringRequest request,
        Func<string, string?, string?, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SuggestedFqn);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LlmModelKey);
        if (request.MaxAttempts < 1)
            throw new ArgumentOutOfRangeException(
                nameof(request), "MaxAttempts must be at least 1.");

        // Resolve the keyed IChatClient by the same service key shape the
        // production LLM neuron grain uses (DigitalBrainAiBridge registers each
        // LlmModel.ServiceKey as a keyed singleton). Resolving from the
        // service provider per-AuthorAsync — rather than capturing in the
        // constructor — lets the gating cluster test register a fresh
        // primed mock per InitializeAsync without re-building the loop.
        var chat = services.GetRequiredKeyedService<IChatClient>(request.LlmModelKey);

        string? lastDraft = null;
        string? lastError = null;

        for (var attempt = 1; attempt <= request.MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (onProgress is not null) await onProgress("Prompting", lastDraft, lastError).ConfigureAwait(false);

            var userMessage = BuildUserMessage(request, attempt, lastError);
            var messages = new[]
            {
                new ChatMessage(ChatRole.System, CreatorInoSystemPrompt.Value),
                new ChatMessage(ChatRole.User, userMessage),
            };

            var response = await chat.GetResponseAsync(messages, options: null, cancellationToken)
                .ConfigureAwait(false);
            var draft = StripCodeFences(response.Text);
            lastDraft = draft;

            if (onProgress is not null) await onProgress("Compiling", draft, null).ConfigureAwait(false);

            var compiled = InoCompiler.Compile(draft, catalog);
            if (!compiled.Success)
            {
                lastError = FormatDiagnostics(compiled.Diagnostics);
                logger.LogInformation(
                    "InoAuthoringLoop attempt {Attempt}: compile failed — {Errors}",
                    attempt, lastError);
                if (onProgress is not null) await onProgress("Compiling", draft, lastError).ConfigureAwait(false);
                continue;
            }

            if (compiled.Plan!.Scenarios.Count == 0)
            {
                lastError = "L6 gate: the document declared zero scenarios — every neuron must carry at least one scenario block.";
                logger.LogInformation(
                    "InoAuthoringLoop attempt {Attempt}: zero scenarios, refusing.",
                    attempt);
                if (onProgress is not null) await onProgress("Compiling", draft, lastError).ConfigureAwait(false);
                continue;
            }

            if (onProgress is not null) await onProgress("Simulating", draft, null).ConfigureAwait(false);

            var report = await SharedRunner.RunAllAsync(compiled.Plan, cancellationToken)
                .ConfigureAwait(false);
            var redResults = report.Results.Where(r => !r.Passed).ToList();
            if (redResults.Count > 0)
            {
                lastError = FormatScenarioFailures(redResults);
                logger.LogInformation(
                    "InoAuthoringLoop attempt {Attempt}: {RedCount}/{TotalCount} scenarios red.",
                    attempt, redResults.Count, report.Results.Count);
                if (onProgress is not null) await onProgress("Simulating", draft, lastError).ConfigureAwait(false);
                continue;
            }

            if (onProgress is not null) await onProgress("Activating", draft, null).ConfigureAwait(false);

            // Green. Persist + return.
            var fqn = compiled.Linked!.Doc.Fqn;
            var neuronId = InoNeuronStore.NeuronIdFromFqn(fqn);
            var sourceFileName = InoNeuronStore.SlugFromFqn(fqn) + ".ino";
            var registration = LinkedPortCatalogContributor.BuildRegistration(draft, compiled.Linked);
            var manifest = new InoNeuronManifest(
                Fqn: fqn,
                NeuronId: neuronId,
                SourceFileName: sourceFileName,
                Intent: request.Intent,
                CreatorLlmModel: request.LlmModelKey,
                CreatedAtUtc: time.GetUtcNow().ToString("O"),
                Incoming: registration.Descriptor.Incoming,
                Outgoing: registration.Descriptor.Outgoing,
                HandledSignalSubscriptions: registration.HandledSignalSubscriptions,
                SourceSha256: InoDefinitionCache.ComputeHash(draft));

            var relativePath = await store.SaveAsync(manifest, draft, cancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation(
                "InoAuthoringLoop promoted {Fqn} after {Attempts} attempt(s).",
                fqn, attempt);

            return new InoAuthoringResult(
                Green: true,
                Attempts: attempt,
                AuthoredFqn: fqn,
                RelativeInoPath: relativePath,
                LastInoSource: draft,
                FinalError: null,
                Registration: registration);
        }

        return new InoAuthoringResult(
            Green: false,
            Attempts: request.MaxAttempts,
            AuthoredFqn: null,
            RelativeInoPath: null,
            LastInoSource: lastDraft,
            FinalError: lastError);
    }

    // The first-attempt user message is the bare intent + suggested FQN.
    // Subsequent attempts prefix `ATTEMPT N — previous compile errors:`
    // followed by a bulleted error list, exactly as the system prompt's
    // iteration contract describes. Keeping this text shape byte-stable
    // is what lets BddMockChatClient prime per-turn fingerprints —
    // exposed (public) so tests can compute the exact fingerprint the
    // loop will hit, rather than guessing message shape.
    public static string BuildUserMessage(InoAuthoringRequest request, int attempt, string? lastError)
    {
        var sb = new StringBuilder();
        if (attempt == 1 || lastError is null)
        {
            sb.Append("Intent: ").Append(request.Intent).Append('\n');
            sb.Append("Suggested FQN: ").Append(request.SuggestedFqn);
            return sb.ToString();
        }

        sb.Append("ATTEMPT ").Append(attempt).Append(" — previous compile errors:\n");
        sb.Append(lastError).Append('\n');
        sb.Append("Intent: ").Append(request.Intent).Append('\n');
        sb.Append("Suggested FQN: ").Append(request.SuggestedFqn);
        return sb.ToString();
    }

    // The system prompt asks for raw .ino. Real LLMs sometimes wrap in
    // ```ino ... ``` or ``` ... ``` anyway; strip a leading/trailing
    // fence so the compiler never sees them. Tolerant of language hints
    // (```ino, ```inolang) and of fences with no language hint.
    internal static string StripCodeFences(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        var trimmed = raw.Trim();

        var firstFence = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (firstFence >= 0)
        {
            var firstLineEnd = trimmed.IndexOf('\n', firstFence);
            if (firstLineEnd >= 0)
            {
                var bodyStart = firstLineEnd + 1;
                var closingFence = trimmed.IndexOf("```", bodyStart, StringComparison.Ordinal);
                if (closingFence >= 0)
                {
                    return trimmed[bodyStart..closingFence].Trim();
                }
                else
                {
                    return trimmed[bodyStart..].Trim();
                }
            }
        }

        return trimmed;
    }

    // Bulleted-line error format the retry user-message body carries.
    // Public so tests can replay the same shape the loop will produce
    // from a given diagnostic bag, without the test having to mirror
    // the formatter independently.
    public static string FormatDiagnostics(IReadOnlyList<Diagnostic> diagnostics) =>
        string.Join('\n', diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => "- " + d.Code + " " + d.Message));

    static string FormatScenarioFailures(IReadOnlyList<ScenarioResult> redResults) =>
        string.Join('\n', redResults
            .Select(r => "- scenario `" + r.Name + "`: " + string.Join(" | ", r.Failures)));
}
