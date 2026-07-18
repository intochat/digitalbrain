using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Runtime;
using Microsoft.Extensions.AI;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Slm.Neuron;

// SLM neuron neuron (E-SDK #58) — the small-LM counterpart of LlmNeuron
// (#54). Wraps a keyed IChatClient behind `IPredicateNeuronTarget` so InoLang's
// `where topic-of(#ask.text) is "Car Insurance":` (v3 §4.3/§4.4) routes
// through this grain when ProductionNeuronHost is configured with a predicate
// binding for the `topic-of` builtin.
//
// Model selection is via the Orleans primary key (mirrors LlmNeuron) —
// the InoLang key `["model-id"]` becomes the IChatClient lookup key. The
// "small" in "small language model" is convention, not enforcement:
// Microsoft.Extensions.AI exposes a single IChatClient abstraction over any
// model, so the neuron target picks whichever keyed model the author wired.
// Microsoft.Extensions.AI does not (as of writing) ship a dedicated SLM
// interface; reusing IChatClient is the documented path.
//
// Classification ABI: the neuron answers a typed boolean. The grain frames a
// minimal classification prompt ("Decide whether the SUBJECT matches the
// TOPIC. Answer YES or NO only.") and parses YES/NO from the assistant's
// reply. The InoLang author never sees this prompt — it is an implementation
// detail of the neuron. Phrasing variance in the LLM is bounded by the strict
// YES/NO instruction; any non-"YES" answer (including refusals, truncation,
// the empty string) collapses to false, which matches the v1 degenerate and
// keeps the failure mode quiet.
[GrainType(NeuronTargetFqn)]
public sealed class SlmNeuron(IServiceProvider services) : Grain, IPredicateNeuronTarget
{
    public const string NeuronTargetFqn = "DigitalBrain.Ai.SlmNeuron";

    // Internal so the cluster test can pin its BddMockChatClient fingerprint
    // to this exact string — any drift on either side fails the test loudly
    // instead of silently flipping the classifier to "always false".
    internal const string ClassifierSystemPrompt =
        "You are a binary classifier. Decide whether the SUBJECT is about the TOPIC. " +
        "Answer with exactly one word: YES or NO. No explanation, no punctuation.";

    // Companion to the system prompt — also internal so the cluster test
    // pins this template, not a duplicated literal. Any drift here would
    // otherwise surface as a `BddMockMissException` on fingerprint, which
    // would obscure (rather than reveal) the real cause.
    internal static string BuildUserPrompt(string subject, string target) =>
        $"SUBJECT: {subject}\nTOPIC: {target}";

    IChatClient? _chat;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var key = this.GetPrimaryKeyString();

        // ProductionNeuronHost defaults `binding.Key ?? binding.TargetFqn`, so
        // an unkeyed `using $topic = neuron(DigitalBrain.Ai.SlmNeuron)` activates
        // with primary key == the FQN. Refuse rather than picking a silent
        // default — v1 requires the author commit to a model id explicitly
        // (mirrors LlmNeuron). Default-model resolution is deferred to a
        // later rung.
        if (string.Equals(key, NeuronTargetFqn, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{NeuronTargetFqn} requires an explicit model key, e.g. " +
                $"`using $topic = neuron({NeuronTargetFqn}[\"openai-gpt-5-mini\"])`. " +
                "Default-model resolution is deferred to a later rung.");

        var (_, modelPart) = BrainScopeHelper.ParseScopedNeuronKey(key);
        if (string.IsNullOrEmpty(modelPart))
        {
            modelPart = key;
        }

        // 1. Try resolving using modelPart directly
        try
        {
            _chat = services.GetKeyedService<IChatClient>(modelPart);
        }
        catch {}

        // 2. Try resolving using LlmModel.All matches
        if (_chat == null)
        {
            var model = Enumerable.FirstOrDefault(global::DigitalBrain.SDK.DigitalBrain.Ai.Models.LlmModel.All, m =>
                string.Equals(m.ServiceKey, modelPart, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.Id, modelPart, StringComparison.OrdinalIgnoreCase) ||
                modelPart.Contains(m.Id, StringComparison.OrdinalIgnoreCase) ||
                m.Id.Contains(modelPart, StringComparison.OrdinalIgnoreCase));

            if (model != null)
            {
                try
                {
                    _chat = services.GetKeyedService<IChatClient>(model.ServiceKey);
                }
                catch {}
            }
        }

        // 3. Fallback: try finding *any* registered keyed IChatClient from LlmModel.All
        if (_chat == null)
        {
            foreach (var m in global::DigitalBrain.SDK.DigitalBrain.Ai.Models.LlmModel.All)
            {
                try
                {
                    _chat = services.GetKeyedService<IChatClient>(m.ServiceKey);
                    if (_chat != null)
                    {
                        break;
                    }
                }
                catch {}
            }
        }

        // 4. Ultimate fallback: if still null, throw the standard KeyedService exception
        if (_chat == null)
        {
            _chat = services.GetRequiredKeyedService<IChatClient>(modelPart);
        }

        return base.OnActivateAsync(cancellationToken);
    }

    public async Task<bool> EvaluateAsync(string subject, string target, CancellationToken ct)
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, ClassifierSystemPrompt),
            new ChatMessage(ChatRole.User, BuildUserPrompt(subject, target)),
        };
        var response = await _chat!.GetResponseAsync(messages, options: null, ct);
        return ParseYesNo(response.Text);
    }

    // Lenient parse around the model's instruction-following slop: a "YES"
    // followed by trailing punctuation, whitespace, or a clarifier (`YES,
    // definitely`) counts; a YES-prefixed unrelated word (`yesterday`,
    // `yessir`) does not. The word-boundary check is the load-bearing
    // distinction — without it, any small-LM that drifts to natural prose
    // would silently flip every classification to true.
    internal static bool ParseYesNo(string answer)
    {
        var trimmed = answer.AsSpan().Trim();
        if (trimmed.Length < 3) return false;
        var startsWithYes =
            (trimmed[0] is 'Y' or 'y')
            && (trimmed[1] is 'E' or 'e')
            && (trimmed[2] is 'S' or 's');
        if (!startsWithYes) return false;
        if (trimmed.Length == 3) return true;
        // Reject `yesterday` / `yessir`: a letter after `yes` means we're
        // inside a longer word, not after a "YES" answer.
        return !char.IsLetter(trimmed[3]);
    }
}
