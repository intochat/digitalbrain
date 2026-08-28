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

    public static BehaviorCorrectionValidation ValidateCorrectionCandidate(
        BehaviorPlan candidate,
        BehaviorPlan parent)
    {
        var structuralFailures = new List<string>();
        foreach (var parentBehavior in parent.Behaviors)
        {
            var retained = candidate.Behaviors.SingleOrDefault(behavior =>
                string.Equals(behavior.Name, parentBehavior.Name, StringComparison.Ordinal));
            if (retained is null || !string.Equals(retained.TriggerKey, parentBehavior.TriggerKey, StringComparison.Ordinal))
            {
                structuralFailures.Add(
                    $"The correction must retain behavior '{parentBehavior.Name}' and its trigger.");
                continue;
            }
            if (!IsOrderedSubsequence(parentBehavior.Steps, retained.Steps))
            {
                structuralFailures.Add(
                    $"The correction must retain all existing steps of behavior '{parentBehavior.Name}' in order.");
                continue;
            }
            if (!OnlyAddsActionSteps(parentBehavior.Steps, retained.Steps))
            {
                structuralFailures.Add(
                    $"The correction may add only action steps to behavior '{parentBehavior.Name}'; new setup, trigger, or filter steps require explicit runtime-proof support.");
            }
        }

        foreach (var parentTest in parent.Tests)
        {
            var retained = candidate.Tests.SingleOrDefault(test =>
                string.Equals(test.Name, parentTest.Name, StringComparison.Ordinal));
            if (retained is null || !retained.Steps.SequenceEqual(parentTest.Steps, BehaviorStepCallComparer.Instance))
            {
                structuralFailures.Add($"The correction must retain parent test '{parentTest.Name}' unchanged.");
            }
        }

        var parentTestNames = parent.Tests.Select(static test => test.Name).ToHashSet(StringComparer.Ordinal);
        var regressionTests = candidate.Tests.Where(test => !parentTestNames.Contains(test.Name)).ToArray();
        if (regressionTests.Length == 0)
        {
            structuralFailures.Add("The correction must add at least one new regression test.");
        }

        var failures = new List<string>();
        foreach (var test in regressionTests)
        {
            var fake = test.Steps.SingleOrDefault(static step => step.Role == BehaviorStepRole.Fake);
            var invoke = test.Steps.SingleOrDefault(static step => step.Role == BehaviorStepRole.Invoke);
            var assertion = test.Steps.SingleOrDefault(static step => step.Role == BehaviorStepRole.Assert);
            var behaviorName = invoke?.Arguments.FirstOrDefault();
            var parentBehavior = parent.Behaviors.SingleOrDefault(behavior =>
                string.Equals(behavior.Name, behaviorName, StringComparison.Ordinal));
            if (fake is null || assertion is null || parentBehavior is null)
            {
                structuralFailures.Add(
                    $"New regression '{test.Name}' must invoke a retained parent behavior.");
                continue;
            }
            if (!FakeMatchesTrigger(fake, parentBehavior.TriggerKey))
            {
                structuralFailures.Add(
                    $"New regression '{test.Name}' must use the retained behavior trigger.");
                continue;
            }
            if (!AssertionMatchesAction(assertion, parentBehavior))
            {
                failures.Add($"{test.Name}: fails against parent revision.");
            }
        }

        return new BehaviorCorrectionValidation(
            structuralFailures.Count == 0,
            structuralFailures,
            new BehaviorTestReport(failures.Count == 0, failures, regressionTests.Length));
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
        if (assertion.Binding == nameof(BuiltInBehaviorSteps.AssertVerifiedSalesforceDescriptionPreserved))
        {
            return behavior.Steps.Any(step =>
                step.Binding == nameof(BuiltInBehaviorSteps.PreserveVerifiedSalesforceFields));
        }
        return false;
    }

    private static bool IsOrderedSubsequence(
        IReadOnlyList<BehaviorStepCall> parent,
        IReadOnlyList<BehaviorStepCall> candidate)
    {
        var candidateIndex = 0;
        foreach (var parentStep in parent)
        {
            while (candidateIndex < candidate.Count
                   && !BehaviorStepCallComparer.Instance.Equals(parentStep, candidate[candidateIndex]))
            {
                candidateIndex++;
            }
            if (candidateIndex == candidate.Count)
            {
                return false;
            }
            candidateIndex++;
        }
        return true;
    }

    private static bool OnlyAddsActionSteps(
        IReadOnlyList<BehaviorStepCall> parent,
        IReadOnlyList<BehaviorStepCall> candidate)
    {
        var parentIndex = 0;
        foreach (var candidateStep in candidate)
        {
            if (parentIndex < parent.Count
                && BehaviorStepCallComparer.Instance.Equals(parent[parentIndex], candidateStep))
            {
                parentIndex++;
                continue;
            }
            if (candidateStep.Role != BehaviorStepRole.Action)
            {
                return false;
            }
        }
        return parentIndex == parent.Count;
    }

    private sealed class BehaviorStepCallComparer : IEqualityComparer<BehaviorStepCall>
    {
        public static BehaviorStepCallComparer Instance { get; } = new();

        public bool Equals(BehaviorStepCall? x, BehaviorStepCall? y)
            => x is not null && y is not null
               && x.Role == y.Role
               && string.Equals(x.Binding, y.Binding, StringComparison.Ordinal)
               && x.Arguments.SequenceEqual(y.Arguments, StringComparer.Ordinal);

        public int GetHashCode(BehaviorStepCall obj) => HashCode.Combine(obj.Role, obj.Binding);
    }
}

internal sealed record BehaviorCorrectionValidation(
    bool StructurallyValid,
    IReadOnlyList<string> StructuralFailures,
    BehaviorTestReport ParentReport);
