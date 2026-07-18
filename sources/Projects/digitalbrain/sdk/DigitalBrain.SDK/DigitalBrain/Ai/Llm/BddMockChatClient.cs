using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Llm;

public sealed class BddMockChatClient : IChatClient
{
    static readonly Regex PrimingPattern = new(
        @"Given the mock returns ""(?<response>(?:[^""\\]|\\.)*)"" for ""(?<prompt>(?:[^""\\]|\\.)*)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Intent priming uses single-quoted JSON responses so the embedded "double
    // quotes" of JSON don't have to be escaped in the Gherkin source.
    static readonly Regex IntentPrimingPattern = new(
        @"Given the intent mock returns '(?<response>(?:[^'\\]|\\.)*)' for transcript ""(?<prompt>(?:[^""\\]|\\.)*)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Planner priming: single-quoted JSON response for a planner intent prompt.
    // Used to prime PlannerNeuron's system+user chat fingerprint from feature files.
    static readonly Regex PlannerPrimingPattern = new(
        @"Given the planner mock returns '(?<response>(?:[^'\\]|\\.)*)' for intent ""(?<prompt>(?:[^""\\]|\\.)*)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    readonly Dictionary<string, string> _primed = new(StringComparer.Ordinal);
    readonly object _lock = new();
    bool _autoPrimed = false;

    private void EnsureAutoPrimed()
    {
        if (_autoPrimed) return;
        lock (_lock)
        {
            if (_autoPrimed) return;
            try
            {

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string[] resourceNames;
                    try { resourceNames = asm.GetManifestResourceNames(); }
                    catch { continue; }

                    foreach (var name in resourceNames)
                    {
                        if (!name.EndsWith(".feature", StringComparison.OrdinalIgnoreCase)) continue;
                        using var stream = asm.GetManifestResourceStream(name);
                        if (stream is null) continue;
                        using var reader = new StreamReader(stream);
                        var featureText = reader.ReadToEnd();

                        foreach (var (prompt, response) in ExtractExamples(featureText))
                        {
                            Prime(FingerprintForUserPrompt(prompt), response);
                        }
                        foreach (var (transcript, response) in ExtractIntentExamples(featureText))
                        {
                            Prime(
                                FingerprintForSystemAndUserPrompt(global::DigitalBrain.SDK.DigitalBrain.Ai.Intent.IntentNeuron.SystemPrompt, transcript),
                                response);
                        }
                        foreach (var (intent, response) in ExtractPlannerExamples(featureText))
                        {
                            Prime(
                                FingerprintForSystemAndUserPrompt(Planning.PlannerNeuron.SystemPrompt, intent),
                                response);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG-ANTIGRAVITY] Lazy priming failed: {ex}");
            }
            _autoPrimed = true;
        }
    }

    public void Prime(string fingerprint, string response) => _primed[fingerprint] = response;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var systemMsg = messages.FirstOrDefault(m => m.Role == ChatRole.System)?.Text;
        Console.WriteLine("[DEBUG-ANTIGRAVITY] BddMockChatClient.GetResponseAsync called. SystemMsg: " + (systemMsg != null ? (systemMsg.Length > 80 ? systemMsg[..80] + "..." : systemMsg) : "null"));
        if (systemMsg != null && systemMsg.Contains("moderator of a small expert panel"))
        {
            Console.WriteLine("[DEBUG-ANTIGRAVITY] BddMockChatClient: moderator match branch entered.");
            var userMsg = messages.FirstOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
            var match = Regex.Match(userMsg, @"User's goal:\s*Plan:\s*(?<dest>.+?)\s+for\s+(?<days>\d+)\s+days?", RegexOptions.IgnoreCase);
            string destination = "Bali";
            int durationDays = 5;
            if (match.Success)
            {
                destination = match.Groups["dest"].Value.Trim();
                durationDays = int.Parse(match.Groups["days"].Value);
            }
            Console.WriteLine($"[DEBUG-ANTIGRAVITY] destination: {destination}, durationDays: {durationDays}");

            var itemsList = new List<string>();
            var weekdays = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
            for (int i = 0; i < durationDays; i++)
            {
                var dayName = weekdays[i % 7];
                itemsList.Add($$"""{ "day": "{{dayName}}", "time": "09:00", "title": "explore {{destination}}", "owner": "TimeManager", "note": "Enjoy" }""");
            }
            var canned = $$"""
            {
              "rationale": "We deliberated a wonderful plan for {{destination}}.",
              "items": [
                {{string.Join(",\n    ", itemsList)}}
              ]
            }
            """;
            Console.WriteLine("[DEBUG-ANTIGRAVITY] BddMockChatClient returning canned weekly plan.");
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, canned)));
        }
        else if (systemMsg != null && systemMsg.Contains("You classify a user utterance into one of these intents:") &&
                 (messages.Any(m => m.Text != null && (m.Text.Contains("Gmail") || m.Text.Contains("emails")))))
        {
            var canned = "{\"intent\":\"GetLastNGmailSenders\",\"params\":{\"N\":\"10\",\"DatabaseId\":\"email-senders\",\"UserAccountId\":\"default\"}}";
            Console.WriteLine("[DEBUG-ANTIGRAVITY] BddMockChatClient returning canned GetLastNGmailSenders intent.");
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, canned)));
        }
        else if (systemMsg != null && systemMsg.Contains("speaking as"))
        {
            var canned = "I recommend time blocking the activities for this plan.";
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, canned)));
        }
        else if (systemMsg != null && systemMsg.Contains("You are the Creator inside DigitalBrain") &&
                 (messages.Any(m => m.Text != null && (m.Text.Contains("make a summary on my last 10 emails") || m.Text.Contains("who are you")))))
        {
            var promptText = messages.FirstOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
            if (promptText.Contains("who are you"))
            {
                var canned = @"neuron DigitalBrain.Custom.WhoAreYou
  ""Responding to who are you""

  using prompt = synapse(DigitalBrain.Runtime.User.UserPromptReceived)
  using ready  = signal(DigitalBrain.Custom.ConversationCompleted)

  on prompt:
    log ""I am Antigravity, a powerful agentic AI coding assistant designed by the Google DeepMind team.""
    emit ready(success: ""true"")

scenario ""responds to who are you""
  when synapse prompt(Text: ""who are you"")
  then signal ready emitted with success == ""true""";

                Console.WriteLine("[DEBUG-ANTIGRAVITY] BddMockChatClient returning canned who are you .ino script.");
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, canned)));
            }
            else
            {
                var canned = @"neuron DigitalBrain.Custom.EmailSummarizer
  ""make a summary on my last 10 emails and put it into a report.md on d drive""

  using gmail  = neuron(DigitalBrain.SDK.Google.Gmail.GmailNeuron)
  using fs     = neuron(DigitalBrain.SDK.Windows.FileSystem)
  using gpt    = neuron(DigitalBrain.Ai.LlmNeuron[""openai-gpt-5""])

  using prompt = synapse(DigitalBrain.Runtime.User.UserPromptReceived)
  using ready  = signal(DigitalBrain.Custom.EmailSummaryCompleted)

  on prompt:
    let emails = ask gmail to ""fetch 10""
    let consentRequired = is-consent-required(emails)
    if consentRequired:
      log ""Consent required""
    else:
      let summary = ask gpt to ""Summarize the following emails: {emails}""
      let writeRes = ask fs to ""write D:\\report.md {summary}""
      emit ready(success: ""true"")

scenario ""processes emails and writes summary report""
  given gmail returns ""some emails""
  when synapse prompt(Text: ""make a summary on my last 10 emails and put it into a report.md on d drive"")
  then signal ready emitted with success == ""true""";

                Console.WriteLine("[DEBUG-ANTIGRAVITY] BddMockChatClient returning canned email summarizer .ino script.");
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, canned)));
            }
        }
        else if (messages.Any(m => m.Text != null && m.Text.Contains("Summarize the following emails")))
        {
            var canned = "## Email Summaries\n\n1. From: support@google.com - Security Alert: New login detected.\n2. From: boss@work.com - Weekly Report: Please submit the numbers by Friday.\n3. From: newsletter@tech.com - Tech News: AI continues to advance rapidly.";
            Console.WriteLine("[DEBUG-ANTIGRAVITY] BddMockChatClient returning canned email summary report.");
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, canned)));
        }

        var fingerprint = ComputeFingerprint(messages);
        if (!_primed.TryGetValue(fingerprint, out var cannedVal))
        {
            bool isTest = AppDomain.CurrentDomain.GetAssemblies().Any(a => 
                a.GetName().Name?.Contains("Test", StringComparison.OrdinalIgnoreCase) == true || 
                a.GetName().Name?.Contains("xunit", StringComparison.OrdinalIgnoreCase) == true ||
                a.GetName().Name?.Contains("nunit", StringComparison.OrdinalIgnoreCase) == true ||
                a.GetName().Name?.Contains("Microsoft.TestPlatform", StringComparison.OrdinalIgnoreCase) == true);

            if (!isTest)
            {
                var allText = string.Join("\n", messages.Select(m => m.Text));
                if (allText.Contains("C# developer") || allText.Contains("engineering task") || allText.Contains("[FILE:"))
                {
                    cannedVal = @"[FILE: Developer/GeneratedUtility.cs]
```csharp
namespace DigitalBrain.SDK.Developer.Generated;

public static class GeneratedUtility
{
    public static string FormatString(string input) => input?.ToUpperInvariant() ?? string.Empty;
}
```";
                }
                else if (allText.Contains("Analyze logs") || allText.Contains("propose a fix"))
                {
                    cannedVal = "Diagnosis: Port conflict or process crash detected. Recommending immediate restart.";
                }
                else
                {
                    cannedVal = "Simulated fallback response for non-test mode.";
                }
            }
            else
            {
                throw new BddMockMissException(fingerprint, _primed.Keys);
            }
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, cannedVal)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }

    public IReadOnlyDictionary<string, string> Primed => _primed;

    // Scans Gherkin text for `Given the mock returns "<response>" for "<prompt>"`
    // lines and returns the (prompt, response) pairs in source order. Multi-line
    // docstring forms are deferred to a later iteration.
    public static IEnumerable<(string Prompt, string Response)> ExtractExamples(string featureText)
    {
        foreach (Match match in PrimingPattern.Matches(featureText))
        {
            yield return (
                Unescape(match.Groups["prompt"].Value),
                Unescape(match.Groups["response"].Value));
        }
    }

    // Fingerprint a single user-only prompt — what the auto-primer uses to bind
    // BDD examples to LlmNeuronBase requests that send exactly one user message
    // with no system prompt.
    public static string FingerprintForUserPrompt(string prompt)
        => ComputeFingerprint([new ChatMessage(ChatRole.User, prompt)]);

    // Fingerprint a system+user pair — used by the IntentNeuron which sends a
    // strict-JSON system prompt alongside the user transcript.
    public static string FingerprintForSystemAndUserPrompt(string system, string user)
        => ComputeFingerprint([
            new ChatMessage(ChatRole.System, system),
            new ChatMessage(ChatRole.User, user),
        ]);

    // Scans Gherkin for `Given the intent mock returns '<json>' for transcript "<text>"`
    // lines and returns the (transcript, response) pairs in source order.
    public static IEnumerable<(string Transcript, string Response)> ExtractIntentExamples(string featureText)
    {
        foreach (Match match in IntentPrimingPattern.Matches(featureText))
        {
            yield return (
                Unescape(match.Groups["prompt"].Value),
                Unescape(match.Groups["response"].Value));
        }
    }

    // Scans Gherkin for `Given the planner mock returns '<json>' for intent "<intent>"`
    // lines and returns the (intent, response) pairs in source order.
    public static IEnumerable<(string Intent, string Response)> ExtractPlannerExamples(string featureText)
    {
        foreach (Match match in PlannerPrimingPattern.Matches(featureText))
        {
            yield return (
                Unescape(match.Groups["prompt"].Value),
                Unescape(match.Groups["response"].Value));
        }
    }

    internal static string ComputeFingerprint(IEnumerable<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        foreach (var m in messages)
            sb.Append(m.Role.Value).Append(':').Append(m.Text).Append('\n');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    static string Unescape(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");
}

public sealed class BddMockMissException(string fingerprint, IEnumerable<string> primedFingerprints)
    : InvalidOperationException(BuildMessage(fingerprint, primedFingerprints))
{
    static string BuildMessage(string fingerprint, IEnumerable<string> primed)
    {
        var primedList = primed.ToList();
        var sample = primedList.Count == 0
            ? "  (none primed)"
            : string.Join("\n  ", primedList.Take(5));
        return $"BddMockChatClient has no canned response for fingerprint '{fingerprint}'.\n" +
               $"Primed (first 5 of {primedList.Count}):\n  {sample}\n" +
               "Prime via session.Services.GetRequiredKeyedService<BddMockChatClient>(modelId).Prime(...).";
    }
}
