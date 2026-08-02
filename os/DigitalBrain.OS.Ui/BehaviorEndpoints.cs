using System.Collections.Concurrent;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Behaviors;
using DigitalBrain.Client;

namespace DigitalBrain.Flutter.Http;

internal static class BehaviorEndpoints
{
    private static readonly ConcurrentDictionary<string, BehaviorChangeProposalDocument> PendingChanges =
        new(StringComparer.Ordinal);

    public static IEndpointRouteBuilder MapBehaviors(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            FlutterHttpContract.BehaviorsPath,
            static async Task<IResult> (
                IDigitalBrain brain,
                IGrainFactory grains,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(brain);
                ArgumentNullException.ThrowIfNull(grains);
                cancellationToken.ThrowIfCancellationRequested();

                var ids = await DiscoverBehaviorIdsAsync(brain, grains, cancellationToken);
                var items = new List<BehaviorLibraryItem>(ids.Count);
                foreach (var behaviorId in ids)
                {
                    var snapshot = await brain.GetGrainProxy<IBehaviorNeuron>(behaviorId).Read();
                    items.Add(ToLibraryItem(behaviorId, snapshot));
                }

                return Results.Ok(new BehaviorLibraryDocument(items));
            });

        endpoints.MapGet(
            FlutterHttpContract.BehaviorPath,
            static async Task<IResult> (
                string behaviorId,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                var snapshot = await brain.GetGrainProxy<IBehaviorNeuron>(behaviorId).Read();
                return Results.Ok(ToDocument(behaviorId, snapshot));
            });

        endpoints.MapGet(
            FlutterHttpContract.BehaviorEventsPath,
            static async Task (
                HttpContext http,
                string behaviorId,
                long? afterSequence,
                OwnerSessionJournal sessionJournal,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(http);
                ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
                ArgumentNullException.ThrowIfNull(sessionJournal);
                cancellationToken.ThrowIfCancellationRequested();

                var cursor = afterSequence.GetValueOrDefault();
                if (cursor < 0)
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                await SseResponse.WriteAsync(
                    http.Response,
                    BehaviorEventFeed.WatchBehaviorAsync(sessionJournal, behaviorId, cursor, cancellationToken),
                    cancellationToken);
            });

        endpoints.MapPost(
            FlutterHttpContract.BehaviorProposePath,
            static async Task<IResult> (
                string behaviorId,
                ProposeBehaviorRequest request,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(request.ProgramSource)
                    || string.IsNullOrWhiteSpace(request.FeatureText))
                {
                    return Results.BadRequest();
                }

                var featureName = string.IsNullOrWhiteSpace(request.FeatureName)
                    ? AccountEnrichmentEditorSeed.FeatureName
                    : request.FeatureName.Trim();
                var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
                    ? behaviorId
                    : request.DisplayName.Trim();
                var description = string.IsNullOrWhiteSpace(request.Description)
                    ? behaviorId
                    : request.Description.Trim();

                var snapshot = await brain.GetGrainProxy<IBehaviorNeuron>(behaviorId).Propose(new ProposeBehaviorRevision(
                    CommandId.New(),
                    request.ProgramSource,
                    new Dictionary<string, string>(StringComparer.Ordinal) { [featureName] = request.FeatureText },
                    displayName,
                    description));

                return Results.Ok(ToDocument(behaviorId, snapshot));
            });

        endpoints.MapPost(
            FlutterHttpContract.BehaviorTestsPath,
            static async Task<IResult> (
                string behaviorId,
                RunBehaviorTestsRequest request,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(request.ArtifactHash))
                {
                    return Results.BadRequest();
                }

                var snapshot = await brain.GetGrainProxy<IBehaviorNeuron>(behaviorId).RunTests(
                    new RunBehaviorTests(CommandId.New(), request.ArtifactHash));
                return Results.Ok(ToDocument(behaviorId, snapshot));
            });

        endpoints.MapPost(
            FlutterHttpContract.BehaviorApprovePath,
            static async Task<IResult> (
                string behaviorId,
                ApproveBehaviorRequest request,
                IDigitalBrain brain,
                IGrainFactory grains,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(brain);
                ArgumentNullException.ThrowIfNull(grains);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(request.ArtifactHash)
                    || !Guid.TryParse(request.ApprovalId, out var approvalIdentity)
                    || approvalIdentity == Guid.Empty)
                {
                    return Results.BadRequest();
                }

                var approval = new BehaviorRevisionApproval(
                    approvalIdentity,
                    CommandId.New(),
                    request.ArtifactHash,
                    ISessionNeuron.ForOwner(brain.Owner),
                    DateTimeOffset.UtcNow);

                var neuron = brain.GetGrainProxy<IBehaviorNeuron>(behaviorId);
                await brain.SendAsync(
                    NeuronId.For<IBehaviorNeuron>(brain.Owner, behaviorId),
                    approval,
                    cancellationToken);

                var session = grains.GetGrain<ISessionNeuron>(ISessionNeuron.ForOwner(brain.Owner).ToGrainId());
                var neuronId = NeuronId.For<IBehaviorNeuron>(brain.Owner, behaviorId);
                var after = 0L;
                for (var attempt = 0; attempt < 50; attempt++)
                {
                    var journal = await session.ReadNeuronJournal(neuronId, JournalKind.Incoming, after);
                    if (journal.Delta.Any(delivery =>
                            delivery.Synapse is BehaviorRevisionApproval recorded
                            && recorded == approval
                            && delivery.Caller == approval.Approver))
                    {
                        break;
                    }

                    after = journal.ResumeSequence;
                    await Task.Delay(20, cancellationToken);
                }

                var snapshot = await neuron.Approve(approval);
                return Results.Ok(ToDocument(behaviorId, snapshot));
            });

        endpoints.MapPost(
            FlutterHttpContract.BehaviorActivatePath,
            static async Task<IResult> (
                string behaviorId,
                ActivateBehaviorRequest request,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(request.ArtifactHash))
                {
                    return Results.BadRequest();
                }

                var snapshot = await brain.GetGrainProxy<IBehaviorNeuron>(behaviorId).Activate(
                    new ActivateBehaviorRevision(CommandId.New(), request.ArtifactHash));
                return Results.Ok(ToDocument(behaviorId, snapshot));
            });

        endpoints.MapPost(
            FlutterHttpContract.BehaviorStopPath,
            static async Task<IResult> (
                string behaviorId,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                var snapshot = await brain.GetGrainProxy<IBehaviorNeuron>(behaviorId).StopRun(
                    new StopBehavior(CommandId.New()));
                return Results.Ok(ToDocument(behaviorId, snapshot));
            });

        endpoints.MapPost(
            FlutterHttpContract.BehaviorStartPath,
            static async Task<IResult> (
                string behaviorId,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                var snapshot = await brain.GetGrainProxy<IBehaviorNeuron>(behaviorId).StartRun(
                    new StartBehavior(CommandId.New()));
                return Results.Ok(ToDocument(behaviorId, snapshot));
            });

        endpoints.MapPost(
            FlutterHttpContract.BehaviorRunOncePath,
            static async Task<IResult> (
                string behaviorId,
                RunOnceBehaviorRequest request,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(request.TriggerTypeName)
                    || string.IsNullOrWhiteSpace(request.TriggerJson))
                {
                    return Results.BadRequest();
                }

                var neuron = brain.GetGrainProxy<IBehaviorNeuron>(behaviorId);
                var executed = await neuron.Execute(new ExecuteBehaviorRevision(
                    CommandId.New(),
                    request.TriggerTypeName,
                    request.TriggerJson));
                var snapshot = await neuron.Read();
                return Results.Ok(new RunOnceBehaviorResult(
                    executed.Succeeded,
                    executed.Outcome ?? string.Empty,
                    ToDocument(behaviorId, snapshot)));
            });

        endpoints.MapPost(
            FlutterHttpContract.BehaviorRollbackPath,
            static async Task<IResult> (
                string behaviorId,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                var snapshot = await brain.GetGrainProxy<IBehaviorNeuron>(behaviorId).Rollback(
                    new RollbackBehaviorRevision(CommandId.New()));
                return Results.Ok(ToDocument(behaviorId, snapshot));
            });

        endpoints.MapGet(
            FlutterHttpContract.BehaviorBindingsPath,
            static async Task<IResult> (
                string behaviorId,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                var document = ToDocument(
                    behaviorId,
                    await brain.GetGrainProxy<IBehaviorNeuron>(behaviorId).Read());
                return Results.Ok(document.Bindings);
            });

        endpoints.MapPost(
            FlutterHttpContract.BehaviorBindingPath,
            static async Task<IResult> (
                string behaviorId,
                string bindingId,
                SetBehaviorBindingRequest request,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
                ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                var snapshot = await brain.GetGrainProxy<IBehaviorNeuron>(behaviorId).SetBindingEnabled(
                    new SetBehaviorBindingEnabled(CommandId.New(), bindingId, request.Enabled));
                return Results.Ok(ToDocument(behaviorId, snapshot));
            });

        endpoints.MapPost(
            FlutterHttpContract.BehaviorChangeProposePath,
            static async Task<IResult> (
                string behaviorId,
                BehaviorChangeProposeRequest request,
                IDigitalBrain brain,
                IBehaviorAuthor author,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(brain);
                ArgumentNullException.ThrowIfNull(author);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(request.RequestText))
                {
                    return Results.BadRequest();
                }

                var current = ToDocument(
                    behaviorId,
                    await brain.GetGrainProxy<IBehaviorNeuron>(behaviorId).Read());
                var featureName = string.IsNullOrWhiteSpace(current.FeatureName)
                    ? "install"
                    : current.FeatureName;
                var authored = author.ProposeScenarios(new BehaviorChangeRequest(
                    behaviorId,
                    request.RequestText.Trim(),
                    current.FeatureText,
                    current.ProgramSource,
                    current.DisplayName,
                    featureName));

                var proposal = new BehaviorChangeProposalDocument(
                    authored.ProposalId,
                    behaviorId,
                    request.RequestText.Trim(),
                    authored.ProposedFeatureText,
                    featureName,
                    Status: "awaiting-scenario-approval",
                    authored.DiffSummary);
                PendingChanges[proposal.ProposalId] = proposal;
                return Results.Ok(proposal);
            });

        endpoints.MapPost(
            FlutterHttpContract.BehaviorChangeApprovePath,
            static async Task<IResult> (
                string behaviorId,
                BehaviorScenarioApprovalRequest request,
                IDigitalBrain brain,
                IBehaviorAuthor author,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(brain);
                ArgumentNullException.ThrowIfNull(author);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(request.ProposalId)
                    || !PendingChanges.TryGetValue(request.ProposalId, out var proposal)
                    || !string.Equals(proposal.BehaviorId, behaviorId, StringComparison.Ordinal))
                {
                    return Results.NotFound();
                }

                if (!request.Approved)
                {
                    PendingChanges.TryRemove(request.ProposalId, out _);
                    return Results.Ok(proposal with { Status = "rejected" });
                }

                var current = ToDocument(
                    behaviorId,
                    await brain.GetGrainProxy<IBehaviorNeuron>(behaviorId).Read());
                var applied = await author.ApplyApprovedScenarios(
                    new BehaviorChangeRequest(
                        behaviorId,
                        proposal.RequestText,
                        current.FeatureText,
                        string.IsNullOrWhiteSpace(current.ProgramSource)
                            ? AccountEnrichmentEditorSeed.ProgramSource
                            : current.ProgramSource,
                        current.DisplayName,
                        proposal.ProposedFeatureName),
                    new BehaviorScenarioProposal(
                        proposal.ProposalId,
                        string.IsNullOrWhiteSpace(request.FeatureText)
                            ? proposal.ProposedFeatureText
                            : request.FeatureText!,
                        proposal.DiffSummary ?? "approved scenario change"),
                    cancellationToken);
                var featureName = string.IsNullOrWhiteSpace(request.FeatureName)
                    ? applied.FeatureName
                    : request.FeatureName;

                var snapshot = await brain.GetGrainProxy<IBehaviorNeuron>(behaviorId).Propose(
                    new ProposeBehaviorRevision(
                        CommandId.New(),
                        applied.ProgramSource,
                        new Dictionary<string, string>(StringComparer.Ordinal) { [featureName] = applied.FeatureText },
                        current.DisplayName,
                        current.Description));

                PendingChanges.TryRemove(request.ProposalId, out _);
                return Results.Ok(ToDocument(behaviorId, snapshot));
            });

        return endpoints;
    }

    private static async Task<IReadOnlyList<string>> DiscoverBehaviorIdsAsync(
        IDigitalBrain brain,
        IGrainFactory grains,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal)
        {
            FlutterHttpContract.AccountEnrichmentBehaviorId,
        };

        var statistics = await grains.GetGrain<IManagementGrain>(0).GetDetailedGrainStatistics();
        cancellationToken.ThrowIfCancellationRequested();

        var ownerPrefix = $"{brain.Owner.Value}/";
        foreach (var statistic in statistics)
        {
            if (!string.Equals(statistic.GrainId.Type.ToString(), "behaviorneuron", StringComparison.Ordinal))
            {
                continue;
            }

            var key = statistic.GrainId.Key.ToString() ?? string.Empty;
            if (!key.StartsWith(ownerPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var name = key[ownerPrefix.Length..];
            if (!string.IsNullOrWhiteSpace(name))
            {
                ids.Add(name);
            }
        }

        return ids.Order(StringComparer.Ordinal).ToArray();
    }

    private static BehaviorLibraryItem ToLibraryItem(string behaviorId, BehaviorSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
        ArgumentNullException.ThrowIfNull(snapshot);

        var document = ToDocument(behaviorId, snapshot);
        var health = document.Status switch
        {
            nameof(BehaviorRevisionStatus.Active) when document.RunState == nameof(BehaviorRunState.Running)
                => "healthy",
            nameof(BehaviorRevisionStatus.Active) when document.RunState is nameof(BehaviorRunState.Stopping)
                or nameof(BehaviorRunState.Stopped)
                => "stopped",
            nameof(BehaviorRevisionStatus.CompileFailed) or nameof(BehaviorRevisionStatus.TestsFailed)
                => "failing",
            nameof(BehaviorRevisionStatus.Empty) => "draft",
            _ => "pending",
        };

        return new BehaviorLibraryItem(
            document.BehaviorId,
            document.DisplayName,
            document.Description,
            document.Status,
            document.RunState,
            document.ActivationGateOpen,
            document.ActiveArtifactHash,
            document.Overview,
            document.Scenarios.Select(static scenario => scenario.Title).ToArray(),
            health);
    }

    private static BehaviorEditorDocument ToDocument(string behaviorId, BehaviorSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
        ArgumentNullException.ThrowIfNull(snapshot);

        var seeded = string.Equals(
            behaviorId,
            FlutterHttpContract.AccountEnrichmentBehaviorId,
            StringComparison.Ordinal);

        var programSource = string.IsNullOrWhiteSpace(snapshot.ProgramSource)
            ? seeded ? AccountEnrichmentEditorSeed.ProgramSource : string.Empty
            : snapshot.ProgramSource;
        var featureName = string.IsNullOrWhiteSpace(snapshot.FeatureName)
            ? seeded ? AccountEnrichmentEditorSeed.FeatureName : "install"
            : snapshot.FeatureName;
        var featureText = string.IsNullOrWhiteSpace(snapshot.FeatureText)
            ? seeded ? AccountEnrichmentEditorSeed.FeatureText : string.Empty
            : snapshot.FeatureText;
        var displayName = string.IsNullOrWhiteSpace(snapshot.DisplayName)
            ? seeded ? AccountEnrichmentEditorSeed.DisplayName : behaviorId
            : snapshot.DisplayName;
        var description = string.IsNullOrWhiteSpace(snapshot.Description)
            ? seeded ? AccountEnrichmentEditorSeed.Description : behaviorId
            : snapshot.Description;
        var overview = string.IsNullOrWhiteSpace(snapshot.Overview)
            ? description
            : snapshot.Overview;

        var scenarios = (snapshot.Scenarios ?? [])
            .Select(static scenario => new BehaviorScenarioDocument(
                scenario.ScenarioId,
                scenario.Title,
                scenario.BindingKey,
                scenario.Passed,
                scenario.Detail))
            .ToArray();
        if (scenarios.Length == 0 && !string.IsNullOrWhiteSpace(featureText))
        {
            scenarios = ParseScenarios(featureText);
        }

        var bindings = (snapshot.Bindings ?? [])
            .Select(static binding => new BehaviorBindingDocument(
                binding.BindingId,
                binding.SourceModule,
                binding.SourceSynapse,
                binding.TargetCase,
                binding.ContractVersion,
                binding.Enabled,
                binding.ConfigurationHint))
            .ToArray();

        var revisions = new List<BehaviorRevisionDocument>();
        if (!string.IsNullOrWhiteSpace(snapshot.ActiveArtifactHash))
        {
            revisions.Add(new BehaviorRevisionDocument(
                "active",
                snapshot.ActiveArtifactHash,
                snapshot.ActiveSignatureHex,
                snapshot.Status.ToString(),
                IsActive: true));
        }

        if (!string.IsNullOrWhiteSpace(snapshot.PriorArtifactHash))
        {
            revisions.Add(new BehaviorRevisionDocument(
                "prior",
                snapshot.PriorArtifactHash,
                SignatureHex: null,
                Status: "superseded",
                IsActive: false));
        }

        if (!string.IsNullOrWhiteSpace(snapshot.ProposedArtifactHash)
            && !string.Equals(snapshot.ProposedArtifactHash, snapshot.ActiveArtifactHash, StringComparison.Ordinal))
        {
            revisions.Add(new BehaviorRevisionDocument(
                "proposed",
                snapshot.ProposedArtifactHash,
                SignatureHex: null,
                snapshot.Status.ToString(),
                IsActive: false));
        }

        return new BehaviorEditorDocument(
            behaviorId,
            snapshot.Status.ToString(),
            snapshot.RunState.ToString(),
            snapshot.ActivationGateOpen,
            snapshot.ProposedArtifactHash,
            snapshot.ActiveArtifactHash,
            snapshot.PriorArtifactHash,
            snapshot.LastCompileFailure,
            snapshot.TestsPassed,
            snapshot.IsApproved,
            snapshot.LastExecutionOutcome,
            programSource,
            featureName,
            featureText,
            displayName,
            description,
            overview,
            snapshot.ActiveSignatureHex,
            snapshot.ActiveTaskCount,
            scenarios,
            bindings,
            revisions);
    }

    private static BehaviorScenarioDocument[] ParseScenarios(string featureText)
    {
        var scenarios = new List<BehaviorScenarioDocument>();
        foreach (var line in featureText.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Scenario", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separator = trimmed.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0 || separator >= trimmed.Length - 1)
            {
                continue;
            }

            var title = trimmed[(separator + 1)..].Trim();
            if (title.Length == 0)
            {
                continue;
            }

            var slugChars = title
                .Select(ch => char.IsAsciiLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')
                .ToArray();
            var slug = new string(slugChars).Trim('-');
            while (slug.Contains("--", StringComparison.Ordinal))
            {
                slug = slug.Replace("--", "-", StringComparison.Ordinal);
            }

            scenarios.Add(new BehaviorScenarioDocument(
                $"scenario.{slug}",
                title,
                $"bind.{slug}",
                Passed: null,
                Detail: null));
        }

        return scenarios.ToArray();
    }

}
