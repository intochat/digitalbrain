using DigitalBrain.SmartPrompt;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using DigitalBrain.Chat;
using DigitalBrain.Abstractions.Identity;
using Xunit;

namespace DigitalBrain.Simulation.Tests.SmartPrompt;

[Collection(SimulationCollection.Name)]
public sealed class BehaviorRuntimeTests(SimulationFixture fixture)
{
    [Fact]
    public async Task Behavior_catalog_persists_generated_behavior_names_without_duplicates()
    {
        var catalog = fixture.Sim.Brain.GetEntity<IBehaviorCatalog>("catalog");
        var name = $"generated-{Guid.NewGuid():N}";

        await catalog.Add(name);
        await catalog.Add(name);

        var state = await catalog.Read();
        Assert.NotNull(state);
        Assert.Equal(1, state!.Names.Count(candidate => candidate == name));
    }

    [Fact]
    public async Task All_eight_seeded_examples_execute_their_paired_fake_scenarios()
    {
        var brain = fixture.Sim.Brain;
        var ingress = fixture.Sim.Grains.GetGrain<IBehaviorIngress>(BehaviorIngressNames.Shared);
        var chat = brain.GetGrainProxy<IChat>("main");
        var beforeChat = (await chat.Read()).Turns.Count;

        foreach (var example in BehaviorExamples.All)
        {
            var definition = await brain.GetEntity<IBehaviorDefinition>(example.Name).Read();
            Assert.NotNull(definition);
            Assert.True(definition!.Active, example.Name);
            Assert.True(definition.LastTest?.AllGreen, example.Name);
            await ingress.Publish(FakeBehaviorEvents.Create(
                example.Name,
                $"paired-{example.Name}-{Guid.NewGuid():N}"));
        }

        var bitcoin = await brain.GetEntity<IChart>("bitcoin_tracker").Read();
        var portfolio = await brain.GetEntity<IChart>("portfolio").Read();
        var health = await brain.GetEntity<IChart>("health").Read();
        Assert.Contains(bitcoin!.Points, static point => point.Value == 95000 && point.SourceUri is not null);
        Assert.Contains(portfolio!.Points, static point => point.Value == 95000 && point.SourceUri is not null);
        Assert.Contains(health!.Points, static point => point.Value == 135 && point.SourceUri is not null);

        var addedTurns = (await chat.Read()).Turns.Skip(beforeChat).Select(static turn => turn.Text).ToArray();
        Assert.Contains(addedTurns, static text => text.StartsWith("Explain urgent work email:", StringComparison.Ordinal));
        Assert.Contains(addedTurns, static text => text.StartsWith("Prepare for a travel event:", StringComparison.Ordinal));
        Assert.Contains(addedTurns, static text => text.StartsWith("Summarize an incoming document:", StringComparison.Ordinal));
        Assert.Contains(addedTurns, static text => text.StartsWith("Triage a new issue:", StringComparison.Ordinal));
        Assert.Contains(addedTurns, static text => text.StartsWith("Remind me when I arrive home:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Activated_X_behavior_routes_one_shared_event_to_one_linked_chart_point()
    {
        var brain = fixture.Sim.Brain;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var chartName = $"bitcoin_tracker_{suffix}";
        var definition = brain.GetEntity<IBehaviorDefinition>($"bitcoin-tracker-{suffix}");
        var example = BehaviorExamples.Find("bitcoin-tracker")!;

        var compilation = await definition.Save(example.Source.Replace(
            "bitcoin_tracker",
            chartName,
            StringComparison.Ordinal));
        Assert.True(compilation.Success);
        var report = await definition.Test();
        Assert.True(report.AllGreen, string.Join(Environment.NewLine, report.Failures));
        await definition.Activate();

        var ingress = fixture.Sim.Grains.GetGrain<IBehaviorIngress>(BehaviorIngressNames.Shared);
        var post = new BehaviorEvent(
            $"post-{suffix}",
            "x.post",
            "elonmusk",
            "Bitcoin reaches 95000",
            95000,
            "https://x.com/elonmusk/status/42",
            DateTimeOffset.Parse("2026-08-25T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        await ingress.Publish(post);
        await ingress.Publish(post);

        var chart = brain.GetEntity<IChart>(chartName);
        var state = await WaitForChart(chart, static candidate => candidate.Points.Count == 1);
        var point = Assert.Single(state.Points);
        Assert.Equal(95000, point.Value);
        Assert.Equal(post.SourceUri, point.SourceUri);
        Assert.Equal(post.EventId, point.EventId);
        Assert.Contains("Test assistant reply", point.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explicit_correction_creates_an_immutable_green_revision_and_undo_restores_the_previous_one()
    {
        var brain = fixture.Sim.Brain;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var definition = brain.GetEntity<IBehaviorDefinition>($"learned-enrichment-{suffix}");
        var firstSource = BehaviorExamples.Find("bitcoin-tracker")!.Source.Replace(
            "bitcoin_tracker",
            $"first_{suffix}",
            StringComparison.Ordinal);
        var correctedSource = AddChatNotificationRegression(
            firstSource,
            $"corrected_{suffix}",
            "A corrected destination receives a notification");

        await definition.Save(firstSource);
        Assert.True((await definition.Test()).AllGreen);
        await definition.Activate();

        var correction = await definition.ApplyCorrection(
            correctedSource,
            "User said to write the verified result to the corrected destination.");

        Assert.Equal(2, correction.Number);
        Assert.True(correction.Test?.AllGreen);
        Assert.Contains("User said", correction.Evidence, StringComparison.Ordinal);
        var corrected = await definition.Read();
        Assert.NotNull(corrected);
        Assert.True(corrected.Active);
        Assert.Equal(2, corrected.ActiveRevision);
        Assert.Equal(2, corrected.Revisions.Count);
        Assert.Equal(firstSource, corrected.Revisions[0].Source);
        Assert.Equal(correctedSource, corrected.Source);

        var restored = await definition.UndoLastCorrection();

        Assert.Equal(1, restored.Number);
        var undone = await definition.Read();
        Assert.NotNull(undone);
        Assert.True(undone.Active);
        Assert.Equal(1, undone.ActiveRevision);
        Assert.Equal(firstSource, undone.Source);
        Assert.Equal(2, undone.Revisions.Count);
    }

    [Fact]
    public async Task Red_correction_does_not_replace_the_active_experience_revision()
    {
        var definition = fixture.Sim.Brain.GetEntity<IBehaviorDefinition>(
            $"rejected-correction-{Guid.NewGuid():N}");
        var source = BehaviorExamples.Find("bitcoin-tracker")!.Source;
        await definition.Save(source);
        Assert.True((await definition.Test()).AllGreen);
        await definition.Activate();

        await Assert.ThrowsAsync<InvalidOperationException>(() => definition.ApplyCorrection(
            "Feature: broken correction",
            "User requested an invalid change."));

        var state = await definition.Read();
        Assert.NotNull(state);
        Assert.True(state.Active);
        Assert.Equal(source, state.Source);
        Assert.Single(state.Revisions);
    }

    [Fact]
    public async Task Saving_a_candidate_does_not_disable_the_active_experience_until_activation()
    {
        var definition = fixture.Sim.Brain.GetEntity<IBehaviorDefinition>(
            $"isolated-candidate-{Guid.NewGuid():N}");
        var original = BehaviorExamples.Find("bitcoin-tracker")!.Source;
        var candidate = original.Replace("bitcoin_tracker", "isolated_candidate", StringComparison.Ordinal);
        await definition.Save(original);
        Assert.True((await definition.Test()).AllGreen);
        await definition.Activate();

        await definition.Save(candidate);

        var whileTesting = await definition.Read();
        Assert.NotNull(whileTesting);
        Assert.True(whileTesting.Active);
        Assert.Equal(original, whileTesting.Source);
        Assert.Equal(2, whileTesting.CandidateRevision);
        Assert.True((await definition.Test()).AllGreen);

        await definition.Activate();
        var activated = await definition.Read();
        Assert.NotNull(activated);
        Assert.True(activated.Active);
        Assert.Equal(candidate, activated.Source);
        Assert.Null(activated.CandidateRevision);
    }

    [Fact]
    public async Task Correction_must_be_red_on_the_parent_before_it_can_learn()
    {
        var definition = fixture.Sim.Brain.GetEntity<IBehaviorDefinition>(
            $"no-regression-{Guid.NewGuid():N}");
        var source = BehaviorExamples.Find("bitcoin-tracker")!.Source;
        await definition.Save(source);
        Assert.True((await definition.Test()).AllGreen);
        await definition.Activate();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => definition.ApplyCorrection(
            source + Environment.NewLine,
            "User repeated the same behavior without specifying a difference."));

        Assert.Contains("regression", exception.Message, StringComparison.OrdinalIgnoreCase);
        var state = await definition.Read();
        Assert.NotNull(state);
        Assert.Equal(1, state.ActiveRevision);
        Assert.Single(state.Revisions);
    }

    [Fact]
    public async Task Unrelated_green_feature_cannot_masquerade_as_a_correction()
    {
        var definition = fixture.Sim.Brain.GetEntity<IBehaviorDefinition>(
            $"unrelated-correction-{Guid.NewGuid():N}");
        var parent = BehaviorExamples.Find("bitcoin-tracker")!.Source;
        var unrelated = BehaviorExamples.Find("urgent-email")!.Source;
        await definition.Save(parent);
        Assert.True((await definition.Test()).AllGreen);
        await definition.Activate();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => definition.ApplyCorrection(
            unrelated,
            "User asked to preserve verified Salesforce fields."));

        Assert.Contains("retain", exception.Message, StringComparison.OrdinalIgnoreCase);
        var state = await definition.Read();
        Assert.NotNull(state);
        Assert.Equal(parent, state.Source);
        Assert.Single(state.Revisions);
    }

    [Fact]
    public async Task Correction_cannot_drop_unasserted_parent_actions()
    {
        var definition = fixture.Sim.Brain.GetEntity<IBehaviorDefinition>(
            $"destructive-correction-{Guid.NewGuid():N}");
        var parent = BehaviorExamples.Find("salesforce-account-enrichment")!.Source;
        var destructive = BehaviorFeatureFallback.ApplyCorrection(
                parent,
                "Preserve verified Salesforce fields when enriching the account.")
            .Replace("Then research the sender company with Web.Agent", "Then preserve verified Salesforce fields",
                StringComparison.Ordinal)
            .Replace("    And enrich Salesforce.Account with verified company research through MCP", "",
                StringComparison.Ordinal);
        await definition.Save(parent);
        Assert.True((await definition.Test()).AllGreen);
        await definition.Activate();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => definition.ApplyCorrection(
            destructive,
            "Preserve fields but accidentally remove enrichment."));

        Assert.Contains("steps", exception.Message, StringComparison.OrdinalIgnoreCase);
        var state = await definition.Read();
        Assert.NotNull(state);
        Assert.Equal(parent, state.Source);
        Assert.Single(state.Revisions);
    }

    [Fact]
    public async Task Correction_cannot_insert_an_unproven_filter_that_blocks_retained_tests()
    {
        var definition = fixture.Sim.Brain.GetEntity<IBehaviorDefinition>(
            $"blocking-filter-{Guid.NewGuid():N}");
        var parent = BehaviorExamples.Find("salesforce-account-enrichment")!.Source;
        var blocked = BehaviorFeatureFallback.ApplyCorrection(
                parent,
                "Preserve verified Salesforce fields when enriching the account.")
            .Replace(
                "    Then research the sender company with Web.Agent",
                "    And the event text contains \"never\"" + Environment.NewLine
                + "    Then research the sender company with Web.Agent",
                StringComparison.Ordinal);
        await definition.Save(parent);
        Assert.True((await definition.Test()).AllGreen);
        await definition.Activate();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => definition.ApplyCorrection(
            blocked,
            "Preserve fields but accidentally block all known examples."));

        Assert.Contains("action steps", exception.Message, StringComparison.OrdinalIgnoreCase);
        var state = await definition.Read();
        Assert.NotNull(state);
        Assert.Equal(parent, state.Source);
        Assert.Single(state.Revisions);
    }

    [Fact]
    public async Task Salesforce_preservation_correction_adds_a_real_parent_red_regression()
    {
        var definition = fixture.Sim.Brain.GetEntity<IBehaviorDefinition>(
            $"salesforce-learning-{Guid.NewGuid():N}");
        var parent = BehaviorExamples.Find("salesforce-account-enrichment")!.Source;
        var corrected = BehaviorFeatureFallback.ApplyCorrection(
            parent,
            "Preserve verified Salesforce fields when enriching the account.");
        await definition.Save(parent);
        Assert.True((await definition.Test()).AllGreen);
        await definition.Activate();

        var revision = await definition.ApplyCorrection(corrected, "Do not overwrite verified Salesforce fields.");

        Assert.Equal(2, revision.Number);
        Assert.False(revision.ParentTest?.AllGreen);
        Assert.Contains(revision.ParentTest!.Failures, failure =>
            failure.Contains("Verified Salesforce description is preserved", StringComparison.Ordinal));
        Assert.Contains("preserve verified Salesforce fields", revision.Source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repeated_undo_walks_revision_parents_instead_of_toggling()
    {
        var definition = fixture.Sim.Brain.GetEntity<IBehaviorDefinition>(
            $"undo-chain-{Guid.NewGuid():N}");
        var first = BehaviorExamples.Find("bitcoin-tracker")!.Source;
        var second = AddChatNotificationRegression(first, "undo_second", "Second correction notifies its chat");
        var third = AddChatNotificationRegression(second, "undo_third", "Third correction notifies its chat");
        await definition.Save(first);
        Assert.True((await definition.Test()).AllGreen);
        await definition.Activate();
        await definition.ApplyCorrection(second, "Use the second chart.");
        await definition.ApplyCorrection(third, "Use the third chart instead.");

        Assert.Equal(2, (await definition.UndoLastCorrection()).Number);
        Assert.Equal(1, (await definition.UndoLastCorrection()).Number);
        var restored = await definition.Read();
        Assert.NotNull(restored);
        Assert.Equal(first, restored.Source);
        Assert.Equal(3, restored.Revisions.Count);
    }

    [Fact]
    public async Task Salesforce_account_enrichment_experience_runs_its_fake_email_through_research_and_crm()
    {
        var brain = fixture.Sim.Brain;
        var example = BehaviorExamples.Find("salesforce-account-enrichment");
        Assert.NotNull(example);
        var definition = await brain.GetEntity<IBehaviorDefinition>(example.Name).Read();
        Assert.NotNull(definition);
        Assert.True(definition.Active);
        Assert.True(definition.LastTest?.AllGreen);

        var chat = brain.GetGrainProxy<IChat>("main");
        var before = (await chat.Read()).Turns.Count;
        await fixture.Sim.Grains.GetGrain<IBehaviorIngress>(BehaviorIngressNames.Shared).Publish(
            FakeBehaviorEvents.Create(example.Name, $"enrichment-{Guid.NewGuid():N}"));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!timeout.IsCancellationRequested)
        {
            var added = (await chat.Read()).Turns.Skip(before).ToArray();
            if (added.Any(turn => turn.Text.Contains("001INTOCHAT", StringComparison.Ordinal)))
            {
                Assert.Contains(added, turn =>
                    turn.Text.Contains("Enrich Salesforce account from a new company email", StringComparison.Ordinal));
                return;
            }
            await Task.Delay(50, timeout.Token);
        }

        throw new TimeoutException("The Salesforce account enrichment experience did not complete.");
    }

    [Fact]
    public async Task User_can_run_account_enrichment_by_talking_only_to_the_assistant()
    {
        var brain = fixture.Sim.Brain;
        var main = brain.GetGrainProxy<IChat>("main");
        var before = (await main.Read()).Turns.Count;
        var requestChat = fixture.Sim.UniqueId("experience-chat");
        await brain.GetGrainProxy<IChat>(requestChat).Send(new SendMessage(
            CommandId.New(),
            "Enrich the company account for the new email from vlad@intochat.io in Salesforce.",
            new ActorContext(new PrincipalId(Guid.NewGuid()), "owner")));

        await ChatTurnDriver.AwaitCompletedTurnAsync(brain, requestChat);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            if ((await main.Read()).Turns.Skip(before)
                .Any(turn => turn.Text.Contains("001INTOCHAT", StringComparison.Ordinal)))
            {
                return;
            }
            await Task.Delay(50, timeout.Token);
        }

        throw new TimeoutException("Assistant chat did not run the account-enrichment experience.");
    }

    private static async Task<ChartState> WaitForChart(IChart chart, Func<ChartState, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!timeout.IsCancellationRequested)
        {
            if (await chart.Read() is { } state && predicate(state))
            {
                return state;
            }
            await Task.Delay(50, timeout.Token);
        }
        throw new TimeoutException("The behavior chart was not updated.");
    }

    private static string AddChatNotificationRegression(string source, string chatName, string testName)
    {
        const string chartActionStart = "    And add UI.Chart.Point to UI.Chart(\"";
        var actionStart = source.IndexOf(chartActionStart, StringComparison.Ordinal);
        Assert.True(actionStart >= 0);
        var actionEnd = source.IndexOf(Environment.NewLine, actionStart, StringComparison.Ordinal);
        Assert.True(actionEnd >= 0);
        var candidate = source.Insert(actionEnd, Environment.NewLine + $"    And notify UI.Chat(\"{chatName}\")");
        return candidate.TrimEnd() + Environment.NewLine
            + $"""

              @test
              Scenario: {testName}
                Given fake X.Post from "elonmusk" with text "Bitcoin reaches 95000" and value 95000
                When behavior "Track Elon posts about Bitcoin" runs
                Then UI.Chat("{chatName}") contains a behavior notification
            """;
    }
}
