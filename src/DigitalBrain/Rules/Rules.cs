namespace DigitalBrain.Rules;

/// <summary>Evaluates rules.</summary>
public static class Rules
{
    /// <summary>Evaluates every rule in order; null when none is broken.</summary>
    /// <param name="operation">The operation being judged.</param>
    /// <param name="rules">The rules to evaluate.</param>
    public static Rejected? Judge(string operation, params ReadOnlySpan<Rule> rules)
    {
        List<string> names = [];
        List<string> messages = [];
        foreach (var rule in rules)
        {
            if (!rule.Broken)
            {
                continue;
            }

            names.Add(rule.Name);
            messages.Add(rule.Message);
        }

        return names.Count == 0 ? null : new Rejected(operation, [.. names], [.. messages]);
    }
}
