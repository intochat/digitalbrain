using System.Text.Json;
using DigitalBrain.Poc.Abstractions;
using DigitalBrain.Poc.Charting;
using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Runtime;
using DigitalBrain.Poc.Social.Contracts;

namespace DigitalBrain.Poc.Host;

internal static class HostScenarioProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static async Task RunVerifiedFixtureAsync(
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken) =>
        await RunAsync(input, output, HostModuleMode.Fixture, cancellationToken);

    internal static async Task RunTrustedQuarantineAsync(
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken) =>
        await RunAsync(input, output, HostModuleMode.Quarantine, cancellationToken);

    internal static async Task RunTrustedActiveAsync(
        TextReader input,
        TextWriter output,
        PocDataRoot root,
        IReadOnlyList<TrustedCandidateRecord> activeCandidates,
        IReadOnlyDictionary<string, string> sessions,
        HostAuthorityLease authority,
        bool allowTestFaults,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(activeCandidates);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(authority);
        var bootstrap = new BootstrapWireRequest(
            Path.GetDirectoryName(root.RootPath) is { } artifacts
                ? Path.GetDirectoryName(artifacts)!
                : throw new InvalidDataException("The active state root is malformed."),
            root.RunId,
            sessions,
            activeCandidates.Select(candidate => new CandidateModuleWire(
                candidate.OwnerId,
                candidate.FamilyId,
                candidate.SourceHash.ToLowerInvariant(),
                Path.Combine(root.CandidateRoot, candidate.CandidateId, candidate.AssemblyPath),
                Path.Combine(root.CandidateRoot, candidate.CandidateId, "candidate.json"),
                candidate.AssemblyHash.ToLowerInvariant(),
                candidate.GrantedInputAliases.ToArray(),
                candidate.GrantedCandidateOutputAliases.ToArray(),
                candidate.GrantedTrustedOutputAliases.ToArray(),
                candidate.GrantedTargetScopes.ToArray()))
                .ToArray(),
            activeCandidates
                .SelectMany(candidate => candidate.GrantedTargetScopes.Select(scope =>
                    new TrustedChartWire(candidate.OwnerId, scope)))
                .Distinct()
                .ToArray());
        var runtime = await HostRuntime.StartAsync(
            bootstrap,
            HostModuleMode.Active,
            allowTestFaults,
            cancellationToken);
        try
        {
            await output.WriteLineAsync(
                JsonSerializer.Serialize(
                    new ActiveHostReady(
                        Environment.ProcessId,
                        activeCandidates.Select(candidate => candidate.SourceHash).ToArray(),
                        runtime.ProjectionBaseUri),
                    JsonOptions).AsMemory(),
                cancellationToken);
            await output.FlushAsync(cancellationToken);
            while (await input.ReadLineAsync(cancellationToken) is { } line)
            {
                ScenarioWireResponse response;
                ScenarioWireRequest? request = null;
                try
                {
                    request = JsonSerializer.Deserialize<ScenarioWireRequest>(line, JsonOptions) ??
                        throw new InvalidDataException("The scenario request was empty.");
                    if (request.Command == "bootstrap")
                    {
                        throw new AuthorizationException(
                            "An active host accepts only its verified pointer selection.");
                    }

                    var payload = request.Command switch
                    {
                        "release-host-authority" => await ReleaseHostAuthorityAsync(
                            authority,
                            AuthorityControl(request),
                            allowTestFaults),
                        "reacquire-host-authority" => await ReacquireHostAuthorityAsync(
                            authority,
                            AuthorityControl(request),
                            cancellationToken),
                        _ => await runtime.ExecuteAsync(request, cancellationToken),
                    };
                    response = ScenarioWireResponse.Ok(request.Id, payload);
                }
                catch (Exception exception)
                {
                    response = ScenarioWireResponse.Failure(
                        request?.Id ?? string.Empty,
                        exception.GetType().Name,
                        exception.Message);
                }

                await output.WriteLineAsync(
                    JsonSerializer.Serialize(response, JsonOptions).AsMemory(),
                    cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
        }
        finally
        {
            await runtime.DisposeAsync();
        }
    }

    private static async Task<object> ReleaseHostAuthorityAsync(
        HostAuthorityLease authority,
        AuthorityControlWire control,
        bool allowTestFaults)
    {
        EnsureAuthorityControl(authority, control);
        await authority.ReleaseActiveAuthorityAsync();
        if (allowTestFaults &&
            string.Equals(
                Environment.GetEnvironmentVariable(ActiveHostBootstrap.TestFaultEnvironment),
                HostFault.AfterAuthorityReleaseBeforeAcknowledgement.ToString(),
                StringComparison.Ordinal))
        {
            throw new IOException("Test fault after releasing host authority before acknowledgement.");
        }

        return new { };
    }

    private static async Task<object> ReacquireHostAuthorityAsync(
        HostAuthorityLease authority,
        AuthorityControlWire control,
        CancellationToken cancellationToken)
    {
        EnsureAuthorityControl(authority, control);
        await authority.ReacquireActiveAuthorityAsync(cancellationToken);
        return new { };
    }

    private static void EnsureAuthorityControl(
        HostAuthorityLease authority,
        AuthorityControlWire control)
    {
        if (!authority.AuthorizesControlToken(control.Token))
        {
            throw new AuthorizationException("The host-authority handoff capability is invalid.");
        }
    }

    private static AuthorityControlWire AuthorityControl(ScenarioWireRequest request) =>
        request.Payload.Deserialize<AuthorityControlWire>(JsonOptions) ?? throw new InvalidDataException(
            "The host-authority handoff capability is empty.");

    private static async Task RunAsync(
        TextReader input,
        TextWriter output,
        HostModuleMode moduleMode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        HostRuntime? runtime = null;
        try
        {
            while (await input.ReadLineAsync(cancellationToken) is { } line)
            {
                ScenarioWireResponse response;
                ScenarioWireRequest? request = null;
                try
                {
                    request = JsonSerializer.Deserialize<ScenarioWireRequest>(line, JsonOptions) ??
                        throw new InvalidDataException("The scenario request was empty.");
                    if (request.Command == "bootstrap")
                    {
                        if (runtime is not null)
                        {
                            throw new InvalidOperationException("The host scenario was already bootstrapped.");
                        }

                        var bootstrap = request.Payload.Deserialize<BootstrapWireRequest>(JsonOptions) ??
                            throw new InvalidDataException("The bootstrap request was empty.");
                        runtime = await HostRuntime.StartAsync(
                            bootstrap,
                            moduleMode,
                            allowTestFaults: false,
                            cancellationToken);
                        response = ScenarioWireResponse.Ok(request.Id, new { processId = Environment.ProcessId });
                    }
                    else
                    {
                        if (runtime is null)
                        {
                            throw new InvalidOperationException("The host scenario must be bootstrapped first.");
                        }

                        var payload = await runtime.ExecuteAsync(request, cancellationToken);
                        response = ScenarioWireResponse.Ok(request.Id, payload);
                    }
                }
                catch (Exception exception)
                {
                    response = ScenarioWireResponse.Failure(
                        request?.Id ?? string.Empty,
                        exception.GetType().Name,
                        exception.Message);
                }

                await output.WriteLineAsync(
                    JsonSerializer.Serialize(response, JsonOptions).AsMemory(),
                    cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
        }
        finally
        {
            if (runtime is not null)
            {
                await runtime.DisposeAsync();
            }
        }
    }

    private sealed class HostRuntime : IAsyncDisposable
    {
        private readonly IReadOnlyDictionary<string, string> _sessions;
        private readonly PocDataRoot _root;
        private readonly DurableTurn _turns;
        private readonly CandidateRuntimeSet _candidates;
        private readonly TrustedChartOutboxDrain _trustedChartOutbox;
        private readonly ChartProjectionEndpoint _charts;
        private readonly ChartProjectionHost _projectionHost;

        private HostRuntime(
            IReadOnlyDictionary<string, string> sessions,
            PocDataRoot root,
            DurableTurn turns,
            CandidateRuntimeSet candidates,
            TrustedChartOutboxDrain trustedChartOutbox,
            ChartProjectionEndpoint charts,
            ChartProjectionHost projectionHost)
        {
            _sessions = sessions;
            _root = root;
            _turns = turns;
            _candidates = candidates;
            _trustedChartOutbox = trustedChartOutbox;
            _charts = charts;
            _projectionHost = projectionHost;
        }

        public Uri ProjectionBaseUri => _projectionHost.BaseUri;

        public static async Task<HostRuntime> StartAsync(
            BootstrapWireRequest bootstrap,
            HostModuleMode moduleMode,
            bool allowTestFaults,
            CancellationToken cancellationToken)
        {
            var root = PocDataRoot.Open(bootstrap.PocRoot, bootstrap.RunId);
            var modules = bootstrap.Modules.Select(module => new VerifiedCandidateModule(
                module.OwnerId,
                CandidateFamilyId.Parse(module.Family),
                module.Revision,
                module.AssemblyPath,
                module.EvidencePath,
                module.AssemblySha256,
                module.GrantedInputAliases,
                module.GrantedOutputAliases,
                module.GrantedTrustedOutputAliases,
                module.GrantedTargetScopes)).ToArray();
            var turns = new DurableTurn(root);
            var projection = new ChartProjectionEndpoint((bootstrap.TrustedCharts ?? [])
                .Select(chart => new ChartNeuron(turns, chart.OwnerId, chart.ChartId)));
            var trustedChartOutbox = new TrustedChartOutboxDrain(
                turns,
                projection,
                TestFaultHooks.CreateAfterChartCommit(
                    moduleMode == HostModuleMode.Active && allowTestFaults));
            var loader = TestFaultHooks.CreateCandidateLoader(
                moduleMode == HostModuleMode.Active && allowTestFaults);
            var candidates = moduleMode switch
            {
                HostModuleMode.Quarantine => await loader.LoadTrustedQuarantineAsync(
                    root,
                    modules,
                    cancellationToken),
                HostModuleMode.Active => await loader.LoadTrustedActiveAsync(
                    root,
                    modules,
                    cancellationToken),
                _ => await loader.LoadVerifiedFixturesAsync(root, modules, cancellationToken),
            };
            var sessions = new Dictionary<string, string>(bootstrap.Sessions, StringComparer.Ordinal);
            var projectionHost = await ChartProjectionHost.StartAsync(
                TestOwnerAuthority.FromExportedSessions(sessions),
                projection,
                cancellationToken);
            var runtime = new HostRuntime(
                sessions,
                root,
                turns,
                candidates,
                trustedChartOutbox,
                projection,
                projectionHost);
            await runtime._candidates.RestoreCommittedOutboxAsync(cancellationToken);
            await runtime._trustedChartOutbox.DrainAsync(cancellationToken);
            return runtime;
        }

        public async Task<object> ExecuteAsync(
            ScenarioWireRequest request,
            CancellationToken cancellationToken) =>
            request.Command switch
            {
                "increment" => await IncrementAsync(
                    Request<FireWireRequest>(request),
                    cancellationToken),
                "throw" => await ThrowAsync(
                    Request<FireWireRequest>(request),
                    cancellationToken),
                "replace-state" => await ReplaceStateAsync(
                    Request<FireWireRequest>(request),
                    cancellationToken),
                "probe" => await ProbeAsync(
                    Request<FireWireRequest>(request),
                    cancellationToken),
                "stage-probe" => await StageProbeAsync(
                    Request<FireWireRequest>(request),
                    cancellationToken),
                "snapshot" => await AuthenticatedSnapshotAsync(request, cancellationToken),
                "journal" => await AuthenticatedJournalAsync(request, cancellationToken),
                "handled-count" => await AuthenticatedHandledCountAsync(request, cancellationToken),
                "turn-count" => await AuthenticatedTurnCountAsync(request, cancellationToken),
                "persisted-candidate-payload" => await AuthenticatedPersistedCandidatePayloadAsync(
                    request,
                    cancellationToken),
                "fire-social" => await FireSocialAsync(
                    Request<SocialWireRequest>(request),
                    cancellationToken),
                "chart-point-count" => await ChartPointCountAsync(
                    Request<ChartCountWireRequest>(request),
                    cancellationToken),
                "chart" => await ChartAsync(
                    Request<ChartCountWireRequest>(request),
                    cancellationToken),
                "journal-for-input" => await JournalForInputAsync(
                    Request<ReceiptWireRequest>(request),
                    cancellationToken),
                "logical-journal" => await LogicalJournalAsync(request, cancellationToken),
                "generated-accepted-count" => await GeneratedAcceptedCountAsync(
                    Request<FamilyWireRequest>(request),
                    cancellationToken),
                "replay-last-chart-delivery" => await ReplayLastChartDeliveryAsync(
                    request,
                    cancellationToken),
                _ => throw new InvalidOperationException(
                    $"Unknown fixed scenario command '{request.Command}'."),
            };

        public async ValueTask DisposeAsync()
        {
            await _projectionHost.DisposeAsync();
            _candidates.Dispose();
        }

        private async Task<object> IncrementAsync(
            FireWireRequest request,
            CancellationToken cancellationToken)
        {
            _ = Authenticate(request.SessionToken);
            await _turns.ExecuteAsync(
                request.ReceiptId,
                "IncrementAndEmit",
                "core-probe-count",
                0,
                async (state, brain) =>
                {
                    state.Replace(state.Value + 1);
                    await brain.FireSynapse(new Emitted(), cancellationToken);
                },
                cancellationToken);
            return new { };
        }

        private async Task<object> ThrowAsync(
            FireWireRequest request,
            CancellationToken cancellationToken)
        {
            _ = Authenticate(request.SessionToken);
            await _turns.ExecuteAsync(
                request.ReceiptId,
                "ThrowAfterStateAndEmit",
                "core-probe-count",
                0,
                async (state, brain) =>
                {
                    state.Replace(state.Value + 1);
                    await brain.FireSynapse(new Emitted(), cancellationToken);
                    throw new ProbeFailureException();
                },
                cancellationToken);
            return new { };
        }

        private async Task<object> ReplaceStateAsync(
            FireWireRequest request,
            CancellationToken cancellationToken)
        {
            _ = Authenticate(request.SessionToken);
            await _turns.ExecuteAsync(
                request.ReceiptId,
                "ReplaceProbeState",
                "core-probe-text",
                string.Empty,
                (state, _) =>
                {
                    state.Replace(request.Value ?? string.Empty);
                    return Task.CompletedTask;
                },
                cancellationToken);
            return new { };
        }

        private async Task<object> ProbeAsync(
            FireWireRequest request,
            CancellationToken cancellationToken)
        {
            var ownerId = Authenticate(request.SessionToken);
            await _candidates.FireTrustedAsync(
                ownerId,
                new ProbeIngress(request.Value ?? string.Empty),
                request.ReceiptId,
                cancellationToken);
            await _trustedChartOutbox.DrainAsync(cancellationToken);
            return new { };
        }

        private async Task<object> StageProbeAsync(
            FireWireRequest request,
            CancellationToken cancellationToken)
        {
            var ownerId = Authenticate(request.SessionToken);
            await _candidates.StageTrustedAsync(
                ownerId,
                new ProbeIngress(request.Value ?? string.Empty),
                request.ReceiptId,
                cancellationToken);
            return new { };
        }

        private async Task<object> FireSocialAsync(
            SocialWireRequest request,
            CancellationToken cancellationToken)
        {
            var ownerId = Authenticate(request.SessionToken);
            await _candidates.FireTrustedAsync(
                ownerId,
                new SocialPostObserved(request.PostId, request.Author, request.OccurredAt),
                request.PostId,
                cancellationToken);
            await _trustedChartOutbox.DrainAsync(cancellationToken);
            return new { };
        }

        private async Task<IntWireResponse> ChartPointCountAsync(
            ChartCountWireRequest request,
            CancellationToken cancellationToken)
        {
            var ownerId = Authenticate(request.SessionToken);
            var snapshot = await _charts.ReadAsync(ownerId, request.ChartId, cancellationToken) ??
                throw new InvalidOperationException("The trusted quarantine chart is not registered.");
            return new IntWireResponse(snapshot.Points.Count);
        }

        private async Task<ChartNeuron.Snapshot> ChartAsync(
            ChartCountWireRequest request,
            CancellationToken cancellationToken)
        {
            var ownerId = Authenticate(request.SessionToken);
            return await _charts.ReadAsync(ownerId, request.ChartId, cancellationToken) ??
                throw new InvalidOperationException("The trusted chart is not registered for this owner.");
        }

        private async Task<IReadOnlyList<string>> JournalForInputAsync(
            ReceiptWireRequest request,
            CancellationToken cancellationToken)
        {
            var ownerId = Authenticate(request.SessionToken);
            return await new Outbox(_root).ReadLogicalJournalKindsForReceiptAsync(
                ownerId,
                request.ReceiptId,
                cancellationToken);
        }

        private async Task<IReadOnlyList<string>> LogicalJournalAsync(
            ScenarioWireRequest request,
            CancellationToken cancellationToken)
        {
            _ = Authenticate(Request<SessionWireRequest>(request).SessionToken);
            return await new Outbox(_root).ReadLogicalJournalKindsAsync(cancellationToken);
        }

        private async Task<IntWireResponse> GeneratedAcceptedCountAsync(
            FamilyWireRequest request,
            CancellationToken cancellationToken)
        {
            var ownerId = Authenticate(request.SessionToken);
            return new IntWireResponse(await new Outbox(_root).ReadGeneratedAcceptedCountAsync(
                ownerId,
                CandidateFamilyId.Parse(request.Family),
                cancellationToken));
        }

        private async Task<object> ReplayLastChartDeliveryAsync(
            ScenarioWireRequest request,
            CancellationToken cancellationToken)
        {
            var ownerId = Authenticate(Request<SessionWireRequest>(request).SessionToken);
            await _trustedChartOutbox.ReplayLastCommittedAsync(ownerId, cancellationToken);
            return new { };
        }

        private async Task<HostSnapshot> SnapshotAsync(CancellationToken cancellationToken) =>
            new(
                await _turns.ReadStateAsync(
                    "core-probe-count",
                    0,
                    cancellationToken),
                (await new Outbox(_root).ReadCommittedAsync(cancellationToken)).Count,
                await new JournalStore(_root).ReadKindsAsync(cancellationToken));

        private Task<HostSnapshot> AuthenticatedSnapshotAsync(
            ScenarioWireRequest request,
            CancellationToken cancellationToken)
        {
            _ = Authenticate(Request<SessionWireRequest>(request).SessionToken);
            return SnapshotAsync(cancellationToken);
        }

        private Task<IReadOnlyList<string>> AuthenticatedJournalAsync(
            ScenarioWireRequest request,
            CancellationToken cancellationToken)
        {
            _ = Authenticate(Request<SessionWireRequest>(request).SessionToken);
            return new JournalStore(_root).ReadKindsAsync(cancellationToken);
        }

        private async Task<IntWireResponse> AuthenticatedHandledCountAsync(
            ScenarioWireRequest request,
            CancellationToken cancellationToken)
        {
            var payload = Request<AliasWireRequest>(request);
            _ = Authenticate(payload.SessionToken);
            return new IntWireResponse(await _candidates.ReadHandledCountAsync(
                payload.ContractAlias,
                cancellationToken));
        }

        private async Task<IntWireResponse> AuthenticatedTurnCountAsync(
            ScenarioWireRequest request,
            CancellationToken cancellationToken)
        {
            var payload = Request<FamilyWireRequest>(request);
            _ = Authenticate(payload.SessionToken);
            return new IntWireResponse(await _candidates.ReadTurnCountAsync(
                CandidateFamilyId.Parse(payload.Family),
                cancellationToken));
        }

        private async Task<PersistedCandidatePayloadView> AuthenticatedPersistedCandidatePayloadAsync(
            ScenarioWireRequest request,
            CancellationToken cancellationToken)
        {
            _ = Authenticate(Request<SessionWireRequest>(request).SessionToken);
            return await _candidates.ReadPersistedCandidatePayloadAsync(cancellationToken);
        }

        private string Authenticate(string token) =>
            _sessions.TryGetValue(token, out var ownerId)
                ? ownerId
                : throw new AuthorizationException("The test scenario session is not authenticated.");

        private static T Request<T>(ScenarioWireRequest request) =>
            request.Payload.Deserialize<T>(JsonOptions) ??
            throw new InvalidDataException($"Scenario payload '{request.Command}' was empty.");
    }

    private sealed record Emitted : Synapse;

    private sealed record HostSnapshot(
        int AcceptedCount,
        int CommittedOutboxCount,
        IReadOnlyList<string> JournalKinds);

    private sealed record IntWireResponse(int Value);

    private sealed record ScenarioWireRequest(string Id, string Command, JsonElement Payload);

    private sealed record AuthorityControlWire(string Token);

    private sealed record ScenarioWireResponse(
        string Id,
        bool Success,
        JsonElement Payload,
        string? ErrorType,
        string? ErrorMessage)
    {
        public static ScenarioWireResponse Ok(string id, object payload) =>
            new(id, true, JsonSerializer.SerializeToElement(payload, JsonOptions), null, null);

        public static ScenarioWireResponse Failure(string id, string errorType, string errorMessage) =>
            new(id, false, JsonSerializer.SerializeToElement(new { }, JsonOptions), errorType, errorMessage);
    }

    private sealed record BootstrapWireRequest(
        string PocRoot,
        string RunId,
        IReadOnlyDictionary<string, string> Sessions,
        CandidateModuleWire[] Modules,
        TrustedChartWire[]? TrustedCharts);

    private sealed record CandidateModuleWire(
        string OwnerId,
        string Family,
        string Revision,
        string AssemblyPath,
        string EvidencePath,
        string AssemblySha256,
        string[] GrantedInputAliases,
        string[] GrantedOutputAliases,
        string[] GrantedTrustedOutputAliases,
        string[] GrantedTargetScopes);

    private sealed record TrustedChartWire(string OwnerId, string ChartId);

    private sealed record ActiveHostReady(
        int ProcessId,
        string[] ActiveSourceHashes,
        Uri ProjectionBaseUri);

    private sealed record FireWireRequest(string SessionToken, string ReceiptId, string? Value);

    private sealed record SessionWireRequest(string SessionToken);

    private sealed record AliasWireRequest(string SessionToken, string ContractAlias);

    private sealed record FamilyWireRequest(string SessionToken, string Family);

    private sealed record ReceiptWireRequest(string SessionToken, string ReceiptId);

    private sealed record SocialWireRequest(
        string SessionToken,
        string PostId,
        string Author,
        DateTimeOffset OccurredAt);

    private sealed record ChartCountWireRequest(string SessionToken, string ChartId);

    private enum HostModuleMode
    {
        Fixture,
        Quarantine,
        Active,
    }

}
