using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using Orleans.Runtime;
using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.SmartPrompt;

[GrainType("behaviordefinition")]
internal sealed class BehaviorDefinitionEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<BehaviorDefinitionState> state,
    IBehaviorCompiler compiler) : Entity<BehaviorDefinitionState>(state), IBehaviorDefinition
{
    public async Task<BehaviorCompilation> Save(string source)
    {
        var compilation = compiler.Compile(source);
        var previous = State is null ? null : Normalize(State);
        var revisions = previous?.Revisions ?? [];
        var parent = previous?.Revisions.SingleOrDefault(candidate => candidate.Number == previous.ActiveRevision);
        var revision = CreateRevision(
            revisions.Count + 1, source, compilation, null, "Saved explicitly.", parent, null);
        if (previous is { Active: true })
        {
            await SaveAsync(previous with
            {
                Revisions = [.. revisions, revision],
                CandidateRevision = revision.Number,
            });
            return compilation;
        }

        await SaveAsync(new BehaviorDefinitionState(
            source,
            compilation,
            Active: false,
            LastTest: null,
            Revisions: [.. revisions, revision],
            ActiveRevision: revision.Number,
            PreviousActiveRevision: parent?.ParentNumber,
            CandidateRevision: revision.Number));
        return compilation;
    }

    public async Task<BehaviorTestReport> Test()
    {
        var current = Normalize(State ?? throw new InvalidOperationException("Save the feature before running its tests."));
        var selectedNumber = current.CandidateRevision ?? current.ActiveRevision;
        var selected = current.Revisions.Single(revision => revision.Number == selectedNumber);
        var report = BehaviorTestInterpreter.Validate(selected.Compilation.Plan, selected.Compilation.Diagnostics);
        var revisions = current.Revisions
            .Select(revision => revision.Number == selectedNumber
                ? revision with { Test = report }
                : revision)
            .ToArray();
        await SaveAsync(current.Active
            ? current with { Revisions = revisions }
            : current with { LastTest = report, Revisions = revisions });
        return report;
    }

    public async Task Activate()
    {
        var current = Normalize(State ?? throw new InvalidOperationException("Save the feature before activating it."));
        if (current.CandidateRevision is int candidateNumber)
        {
            var candidate = current.Revisions.Single(revision => revision.Number == candidateNumber);
            var tested = candidate.Test ?? await Test();
            current = Normalize(State!);
            candidate = current.Revisions.Single(revision => revision.Number == candidateNumber);
            if (!tested.AllGreen || candidate.Compilation.Plan is null)
            {
                throw new InvalidOperationException("A behavior can activate only after all paired scenarios are green.");
            }

            var activated = current with
            {
                Source = candidate.Source,
                Compilation = candidate.Compilation,
                Active = true,
                LastTest = candidate.Test,
                ActiveRevision = candidate.Number,
                PreviousActiveRevision = current.Active ? current.ActiveRevision : candidate.ParentNumber,
                CandidateRevision = null,
            };
            await ChangeSubscriptions(activated, subscribe: true);
            await SaveAsync(activated);
            if (current.Active && current.ActiveRevision != candidate.Number)
            {
                await ChangeSubscriptions(current, subscribe: false);
            }
            return;
        }
        if (current.Active)
        {
            return;
        }

        var active = current.Revisions.Single(revision => revision.Number == current.ActiveRevision);
        if (active.Test is not { AllGreen: true } || active.Compilation.Plan is null)
        {
            throw new InvalidOperationException("A behavior can activate only after all paired scenarios are green.");
        }
        await ChangeSubscriptions(current, subscribe: true);
        await SaveAsync(current with { Active = true });
    }

    public async Task Disable()
    {
        if (State is not { Active: true } stored)
        {
            return;
        }
        var current = Normalize(stored);
        await ChangeSubscriptions(current, subscribe: false);
        await SaveAsync(current with { Active = false });
    }

    public async Task<BehaviorRevision> ApplyCorrection(string source, string evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence);

        var current = Normalize(State ?? throw new InvalidOperationException(
            "Save the feature before applying a correction."));
        var compilation = compiler.Compile(source);
        var report = BehaviorTestInterpreter.Validate(compilation.Plan, compilation.Diagnostics);
        if (!report.AllGreen || compilation.Plan is null)
        {
            throw new InvalidOperationException(
                "The correction was not activated because its paired scenarios are red: "
                + string.Join("; ", report.Failures));
        }

        if (current.Compilation.Plan is not { } parentPlan)
        {
            throw new InvalidOperationException("The active parent revision has no executable plan.");
        }
        var validation = BehaviorTestInterpreter.ValidateCorrectionCandidate(compilation.Plan, parentPlan);
        if (!validation.StructurallyValid)
        {
            throw new InvalidOperationException(
                "The correction must retain the active Experience and its existing tests: "
                + string.Join("; ", validation.StructuralFailures));
        }
        var parentReport = validation.ParentReport;
        if (parentReport.AllGreen)
        {
            throw new InvalidOperationException(
                "The correction was not learned because its regression is still green on the parent revision.");
        }

        var parent = current.Revisions.Single(revision => revision.Number == current.ActiveRevision);
        var revision = CreateRevision(
            current.Revisions.Count + 1, source, compilation, report, evidence, parent, parentReport);
        var corrected = new BehaviorDefinitionState(
            source,
            compilation,
            Active: true,
            LastTest: report,
            Revisions: [.. current.Revisions, revision],
            ActiveRevision: revision.Number,
            PreviousActiveRevision: parent.Number,
            CandidateRevision: null);

        await ChangeSubscriptions(corrected, subscribe: true);
        await SaveAsync(corrected);
        if (current.Active)
        {
            await ChangeSubscriptions(current, subscribe: false);
        }
        return revision;
    }

    public async Task<BehaviorRevision> UndoLastCorrection()
    {
        var current = Normalize(State ?? throw new InvalidOperationException(
            "Save the feature before undoing a correction."));
        var activeRevision = current.Revisions.Single(revision => revision.Number == current.ActiveRevision);
        if (activeRevision.ParentNumber is not int previousNumber)
        {
            throw new InvalidOperationException("There is no previous active revision to restore.");
        }

        var previous = current.Revisions.SingleOrDefault(revision => revision.Number == previousNumber)
            ?? throw new InvalidOperationException($"Revision {previousNumber} no longer exists.");
        if (previous.Test is not { AllGreen: true })
        {
            throw new InvalidOperationException($"Revision {previousNumber} is not green and cannot be restored.");
        }

        var restored = new BehaviorDefinitionState(
            previous.Source,
            previous.Compilation,
            Active: true,
            LastTest: previous.Test,
            Revisions: current.Revisions,
            ActiveRevision: previous.Number,
            PreviousActiveRevision: previous.ParentNumber,
            CandidateRevision: null);
        await ChangeSubscriptions(restored, subscribe: true);
        await SaveAsync(restored);
        if (current.Active)
        {
            await ChangeSubscriptions(current, subscribe: false);
        }
        return previous;
    }

    private async Task ChangeSubscriptions(BehaviorDefinitionState definition, bool subscribe)
    {
        if (definition.Compilation.Plan is not { } plan)
        {
            return;
        }

        var (owner, name) = Address();
        foreach (var scenario in plan.Behaviors)
        {
            var subscription = new BehaviorSubscription(owner, name, scenario.Name, plan.SourceHash);
            var directory = GrainFactory.GetGrain<IBehaviorTriggerDirectory>(scenario.TriggerKey);
            if (subscribe)
            {
                await directory.Subscribe(subscription);
            }
            else
            {
                await directory.Unsubscribe(subscription);
            }
        }
    }

    private (string Owner, string Name) Address()
    {
        var key = this.GetPrimaryKeyString();
        var separator = key.IndexOf('/', StringComparison.Ordinal);
        if (separator <= 0 || separator == key.Length - 1)
        {
            throw new InvalidOperationException($"Behavior definition key '{key}' is not owner-scoped.");
        }
        _ = new OwnerId(key[..separator]);
        return (key[..separator], key[(separator + 1)..]);
    }

    private static BehaviorDefinitionState Normalize(BehaviorDefinitionState state)
    {
        if (state.Revisions is { Count: > 0 })
        {
            return state;
        }

        var revision = CreateRevision(
            1, state.Source, state.Compilation, state.LastTest,
            "Imported from the original behavior definition.", null, null);
        return state with
        {
            Revisions = [revision],
            ActiveRevision = 1,
            CandidateRevision = null,
        };
    }

    private static BehaviorRevision CreateRevision(
        int number,
        string source,
        BehaviorCompilation compilation,
        BehaviorTestReport? test,
        string evidence,
        BehaviorRevision? parent,
        BehaviorTestReport? parentTest)
    {
        var sourceHash = compilation.Plan?.SourceHash
            ?? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        return new BehaviorRevision(
            number,
            source,
            compilation,
            test,
            evidence,
            DateTimeOffset.UtcNow,
            sourceHash,
            parent?.Number,
            parent?.SourceHash,
            parentTest);
    }
}
