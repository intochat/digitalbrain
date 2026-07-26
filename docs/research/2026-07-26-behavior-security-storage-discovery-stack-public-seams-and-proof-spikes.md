## Public seams

The runtime and domain should see no Windows, Azure, COSE, JSON Schema, or vector-provider types:

```csharp
public interface IBehaviorSandbox
{
    ValueTask<BehaviorExecutionResult> ExecuteAsync(
        ApprovedBehaviorExecution request,
        CancellationToken cancellationToken);
}

public interface IBehaviorArtifactStore
{
    ValueTask PutAsync(
        BehaviorArtifactDigest digest,
        Stream exactArtifact,
        CancellationToken cancellationToken);

    ValueTask<BehaviorArtifactLease> OpenVerifiedAsync(
        BehaviorArtifactDigest digest,
        CancellationToken cancellationToken);
}

public interface IArtifactSignatureVerifier
{
    ValueTask<ArtifactProvenance> VerifyAsync(
        BehaviorArtifactDigest digest,
        Stream exactArtifact,
        ArtifactSignature signature,
        CancellationToken cancellationToken);
}

public interface IIntentSchemaValidator
{
    IntentSchemaCompilation Compile(
        IntentSchemaSource source,
        IntentSchemaPolicy policy);

    IntentValidationResult Validate(
        IntentSchemaCompilation schema,
        JsonElement intent);
}

public interface ICatalogCandidateDiscovery
{
    ValueTask<IReadOnlyList<CatalogCandidate>> FindAsync(
        CatalogDiscoveryQuery query,
        CancellationToken cancellationToken);
}
```

These are architectural sketches, not permission to create one project per interface. Keep each
interface with the consumer that needs it and group closely related Windows implementation
details behind one deep adapter.

## Mandatory proof spikes before production implementation

1. **Real LPAC worker:** launch the self-contained .NET 10 worker through generated CsWin32 APIs,
   verify token evidence, load one admitted DLL, call the broker, and prove all negative-access
   cases.
2. **Mitigation matrix:** test each proposed process mitigation independently and in the supported
   combination; record the resulting policy as versioned admission/execution evidence.
3. **Job containment:** prove atomic job assignment, active-process restriction, memory/CPU
   enforcement, deadline kill, and kill-on-broker-exit.
4. **Pipe authorization:** prove exact SID DACL, one-use handshake, bounded buffers/messages,
   cross-execution rejection, cancellation, and malformed-request behavior.
5. **Blob race and tamper:** against Azurite, race two create-only writers and require one success
   plus one verified 412; reject a mismatched existing blob and a corrupted download. Run an Azure
   service integration test for any feature whose Azurite parity is not documented.
6. **COSE profile:** verify Windows/Linux interoperability, swapped bytes, altered associated
   data, unknown key ID, revoked key, malformed message, and key rotation.
7. **Schema profile:** run the official 2020-12 suite and adversarial resource-limit cases; resolve
   the `JsonSchema.Net` license decision before taking a binary dependency.
8. **Discovery benchmark:** prove that vector infrastructure improves reviewed recall or latency
   before selecting a provider.

## Plan changes caused by this research

1. Add a Windows-only sandbox prototype before implementing the general Behavior execution
   pipeline. Runtime assembly loading alone cannot be accepted as a boundary.
2. Make the initial worker self-contained and one-execution-per-process. Reuse is an optimization
   that must preserve the same evidence and cleanup guarantees.
3. Add `Microsoft.Windows.CsWin32` only to the launcher and use atomic Job Object assignment at
   process creation.
4. Use Kestrel HTTP/2 over a `LOCAL\` named pipe with exact DACL and bounded buffers instead of a
   custom IPC framing implementation.
5. Add a separate Behavior-artifact blob container and domain adapter; preserve the journal as the
   authorization authority.
6. Bind approval to SHA-256 of the exact artifact envelope. Add COSE only for publisher
   provenance, behind a trust-store seam.
7. Put draft-2020-12 validation behind a restricted DigitalBrain schema profile. Resolve the
   current `JsonSchema.Net` binary license before implementation.
8. Implement deterministic catalog discovery first. Defer vector packages/providers until the
   benchmark proves a need, and keep any future index rebuildable and non-authoritative.
9. Make Hyper-V/VM isolation a separate hosted-execution tier rather than overstating the LPAC
   boundary.
