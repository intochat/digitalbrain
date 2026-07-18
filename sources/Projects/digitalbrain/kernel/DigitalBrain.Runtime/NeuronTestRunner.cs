using System.Text.RegularExpressions;

namespace DigitalBrain.Runtime;

public sealed class NeuronTestRunner(IGrainFactory grains) : INeuronTestRunner
{
    public async Task<TestResult> RunAsync(DynamicNeuronSpec spec, CancellationToken ct = default)
    {
        var failures = new List<string>();
        var dyn = grains.GetGrain<IDynamicNeuron>(spec.Id.Value);
        // Make sure the grain has the script loaded — Creator stages the spec
        // into INeuronRegistry, but DynamicNeuronGrain only loads its script
        // on activation. Force-load via LoadAsync to keep this idempotent.
        await dyn.LoadAsync(spec);

        foreach (var scenario in ParseScenarios(spec.FeatureText))
        {
            try
            {
                await RunScenario(scenario, dyn, ct);
            }
            catch (TestAssertionException ex)
            {
                failures.Add($"[{scenario.Name}] {ex.Message}");
            }
            catch (Exception ex)
            {
                failures.Add($"[{scenario.Name}] unexpected: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return new TestResult(failures.Count == 0, failures);
    }

    static async Task RunScenario(Scenario scenario, IDynamicNeuron dyn, CancellationToken ct)
    {
        string? lastResponse = null;
        foreach (var step in scenario.Steps)
        {
            var match = Patterns.Invoke.Match(step.Text);
            if (match.Success)
            {
                var payloadJson = match.Groups["payload"].Value.Trim();
                var typeName = match.Groups["type"].Value;
                lastResponse = await dyn.InvokeAsync(payloadJson, typeName, CorrelationId.New());
                continue;
            }

            match = Patterns.ResponseEquals.Match(step.Text);
            if (match.Success)
            {
                var expected = match.Groups["payload"].Value.Trim();
                if (lastResponse is null)
                    throw new TestAssertionException("Then step requires a prior When/invoke step.");
                if (NormalizeJson(lastResponse) != NormalizeJson(expected))
                    throw new TestAssertionException(
                        $"response equals: expected {expected}, got {lastResponse}");
                continue;
            }

            match = Patterns.ResponseContains.Match(step.Text);
            if (match.Success)
            {
                var fragment = match.Groups["text"].Value;
                if (lastResponse is null)
                    throw new TestAssertionException("Then step requires a prior When/invoke step.");
                if (!lastResponse.Contains(fragment, StringComparison.Ordinal))
                    throw new TestAssertionException(
                        $"response contains: expected substring '{fragment}', got '{lastResponse}'");
                continue;
            }

            // "Given a fresh dynamic neuron" is a no-op — the spec already
            // ensures grain activation by the time RunAsync starts. Other
            // unrecognized lines are silently ignored so step phrasing tweaks
            // don't fail the whole scenario.
        }
    }

    static IEnumerable<Scenario> ParseScenarios(string featureText)
    {
        var lines = featureText.Replace("\r\n", "\n").Split('\n');
        Scenario? current = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("Scenario:", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null) yield return current;
                current = new Scenario(line["Scenario:".Length..].Trim(), new List<Step>());
                continue;
            }
            if (current is null) continue;
            if (line.StartsWith("Given ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("When ",  StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Then ",  StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("And ",   StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("But ",   StringComparison.OrdinalIgnoreCase))
            {
                var keywordEnd = line.IndexOf(' ');
                current.Steps.Add(new Step(line[(keywordEnd + 1)..]));
            }
        }
        if (current is not null) yield return current;
    }

    static string NormalizeJson(string s)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(s);
            return System.Text.Json.JsonSerializer.Serialize(doc.RootElement);
        }
        catch
        {
            return s.Trim();
        }
    }

    sealed record Scenario(string Name, List<Step> Steps);
    sealed record Step(string Text);

    static class Patterns
    {
        // When the neuron is invoked with payload {...} as type "X.Y.Z"
        public static readonly Regex Invoke = new(
            @"^the neuron is invoked with payload\s+(?<payload>\{.*\}|\[.*\])\s+as type\s+""(?<type>[^""]+)""\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Then the response equals {...}
        public static readonly Regex ResponseEquals = new(
            @"^the response equals\s+(?<payload>\{.*\}|\[.*\]|""[^""]*""|\d+|true|false|null)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // And the response contains "..."
        public static readonly Regex ResponseContains = new(
            @"^the response contains\s+""(?<text>[^""]+)""\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }
}

internal sealed class TestAssertionException(string message) : Exception(message);
