using System.Text.Json;
using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Creator;
using DigitalBrain.Poc.Host;
using DigitalBrain.Poc.Runtime;
using DigitalBrain.Poc.Social.Contracts;

namespace DigitalBrain.Poc.Flutter.Fixture;

internal static class FlutterIntegrationFixture
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(
        TextReader input,
        TextWriter output,
        string pocRoot,
        bool emitMalformedPostReadinessRecord,
        CancellationToken cancellationToken)
    {
        var root = PocDataRoot.Create(pocRoot);
        HostSupervisor? supervisor = null;
        var exitCode = 0;
        try
        {
            var owners = new TestOwnerAuthority();
            var attestations = owners.CreateAttestationSigner();
            var approvals = owners.CreateOwnerApprovalSigner();
            var pointers = owners.CreatePointerSigner();
            var store = new TrustedCandidateCatalogStore(root, attestations, approvals, pointers);
            var catalog = new CandidateCatalog(store);
            var repository = new CandidateRepository();
            var compiled = await new FileCandidateCompiler(repository).CompileAsync(
                ElonChartAuthoringIntent.DefaultTrustedFixture,
                root,
                cancellationToken);
            await new QuarantineRunner(
                repository,
                store,
                attestations,
                owners.ExportSessions()).RunTrustedFixtureAsync(compiled, root, cancellationToken);
            var owner = owners.PrincipalForTest("owner-a");
            await catalog.ApproveAsync(owner, compiled.Id, cancellationToken);
            supervisor = new HostSupervisor(root, store, pointers, owners);
            var promotion = await supervisor.PromoteAsync(
                owner,
                compiled.Id,
                cancellationToken: cancellationToken);
            if (!promotion.Succeeded)
            {
                throw new InvalidOperationException($"Flutter fixture promotion failed: {promotion.Failure}.");
            }

            var attachment = promotion.Attachment!;
            var session = owners.SessionFor("owner-a");
            await WriteAsync(
                output,
                new ReadyWire(
                    "ready",
                    attachment.ProjectionBaseUri,
                    session.Token,
                    root.RunId,
                    attachment.ProcessId),
                cancellationToken);
            if (emitMalformedPostReadinessRecord)
            {
                await WriteAsync(output, new MalformedWire("malformed"), cancellationToken);
            }
            while (await input.ReadLineAsync(cancellationToken) is { } line)
            {
                var request = JsonSerializer.Deserialize<RequestWire>(line, JsonOptions) ??
                    throw new InvalidDataException("Flutter fixture request was empty.");
                if (request.Command == "shutdown")
                {
                    await WriteAsync(output, new ResponseWire(request.Id, true, null), cancellationToken);
                    break;
                }

                if (request.Command != "fire-social" ||
                    string.IsNullOrWhiteSpace(request.Author) ||
                    string.IsNullOrWhiteSpace(request.PostId))
                {
                    await WriteAsync(
                        output,
                        new ResponseWire(request.Id, false, "Unsupported fixture command."),
                        cancellationToken);
                    continue;
                }

                await attachment.FireTrustedAsync(
                    session,
                    new SocialPostObserved(request.PostId, request.Author, DateTimeOffset.UtcNow),
                    cancellationToken);
                await WriteAsync(output, new ResponseWire(request.Id, true, null), cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            exitCode = 3;
            await WriteAsync(
                output,
                new FailureWire("failure", exception.GetType().Name, exception.Message),
                CancellationToken.None);
        }
        finally
        {
            Exception? teardownFailure = null;
            try
            {
                if (supervisor is not null)
                {
                    await supervisor.DisposeAsync();
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                teardownFailure = exception;
            }

            var runId = root.RunId;
            try
            {
                await root.DisposeAsync();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                teardownFailure ??= exception;
            }

            IReadOnlyList<string> artifacts;
            try
            {
                artifacts = await PocDataRoot.FindArtifactsForRunAsync(
                    pocRoot,
                    runId,
                    CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                artifacts = [];
                teardownFailure ??= exception;
            }
            if (artifacts.Count != 0)
            {
                exitCode = 4;
            }

            if (teardownFailure is not null)
            {
                exitCode = 4;
                await WriteAsync(
                    output,
                    new FailureWire(
                        "failure",
                        teardownFailure.GetType().Name,
                        teardownFailure.Message),
                    CancellationToken.None);
            }

            await WriteAsync(
                output,
                new DisposedWire("disposed", artifacts.ToArray()),
                CancellationToken.None);
        }

        return exitCode;
    }

    private static async Task WriteAsync(
        TextWriter output,
        object value,
        CancellationToken cancellationToken)
    {
        await output.WriteLineAsync(
            JsonSerializer.Serialize(value, JsonOptions).AsMemory(),
            cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private sealed record ReadyWire(
        string Kind,
        Uri BaseUri,
        string OwnerSessionToken,
        string RunId,
        int ProcessId);

    private sealed record RequestWire(
        string Id,
        string Command,
        string? Author,
        string? PostId);

    private sealed record ResponseWire(string Id, bool Success, string? Error);

    private sealed record FailureWire(string Kind, string ErrorType, string Error);

    private sealed record MalformedWire(string Kind);

    private sealed record DisposedWire(string Kind, string[] Artifacts);
}
