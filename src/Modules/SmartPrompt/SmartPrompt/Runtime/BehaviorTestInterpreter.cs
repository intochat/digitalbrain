namespace DigitalBrain.SmartPrompt;

internal static class BehaviorTestInterpreter
{
    public static BehaviorTestReport Validate(BehaviorPlan? plan, IReadOnlyList<BehaviorDiagnostic> diagnostics)
    {
        var failures = diagnostics
            .Where(static diagnostic => diagnostic.Severity == BehaviorDiagnosticSeverity.Error)
            .Select(static diagnostic => $"line {diagnostic.Line}: {diagnostic.Message}")
            .ToList();
        if (plan is null)
        {
            return new BehaviorTestReport(false, failures, 0);
        }

        foreach (var test in plan.Tests)
        {
            var fake = test.Steps.SingleOrDefault(static step => step.Role == BehaviorStepRole.Fake);
            var invoke = test.Steps.SingleOrDefault(static step => step.Role == BehaviorStepRole.Invoke);
            var assertion = test.Steps.SingleOrDefault(static step => step.Role == BehaviorStepRole.Assert);
            if (fake is null || invoke is null || assertion is null)
            {
                failures.Add($"{test.Name}: expected exactly one fake, invocation, and assertion step.");
                continue;
            }

            var behaviorName = invoke.Arguments.FirstOrDefault();
            var behavior = plan.Behaviors.SingleOrDefault(candidate =>
                string.Equals(candidate.Name, behaviorName, StringComparison.Ordinal));
            if (behavior is null)
            {
                failures.Add($"{test.Name}: behavior '{behaviorName}' does not exist.");
                continue;
            }

            if (!FakeMatchesTrigger(fake, behavior.TriggerKey))
            {
                failures.Add($"{test.Name}: fake event does not match trigger '{behavior.TriggerKey}'.");
            }
            if (!AssertionMatchesAction(assertion, behavior))
            {
                failures.Add($"{test.Name}: assertion does not observe an action from '{behavior.Name}'.");
            }
        }

        return new BehaviorTestReport(failures.Count == 0 && plan.Tests.Count > 0, failures, plan.Tests.Count);
    }

    private static bool FakeMatchesTrigger(BehaviorStepCall fake, string triggerKey)
    {
        string kind;
        string source;
        if (fake.Binding == nameof(BuiltInBehaviorSteps.FakeXPost) && fake.Arguments.Count >= 1)
        {
            kind = "x.post";
            source = fake.Arguments[0];
        }
        else if (fake.Binding == nameof(BuiltInBehaviorSteps.FakeEvent) && fake.Arguments.Count >= 2)
        {
            kind = fake.Arguments[0];
            source = fake.Arguments[1];
        }
        else
        {
            return false;
        }
        var probe = new BehaviorEvent("test", kind, source, "test", 1, "digitalbrain://test", DateTimeOffset.UnixEpoch);
        return string.Equals(probe.TriggerKey, triggerKey, StringComparison.Ordinal);
    }

    private static bool AssertionMatchesAction(BehaviorStepCall assertion, BehaviorScenarioPlan behavior)
    {
        if (assertion.Binding == nameof(BuiltInBehaviorSteps.AssertChartPoint))
        {
            return behavior.Steps.Any(step => step.Binding == nameof(BuiltInBehaviorSteps.AddChartPoint)
                && step.Arguments.SequenceEqual(assertion.Arguments.Take(1)));
        }
        if (assertion.Binding == nameof(BuiltInBehaviorSteps.AssertChatNotification))
        {
            return behavior.Steps.Any(step => step.Binding == nameof(BuiltInBehaviorSteps.NotifyChat)
                && step.Arguments.SequenceEqual(assertion.Arguments));
        }
        return false;
    }
}
