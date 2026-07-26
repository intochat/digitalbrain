# Behavior security, artifact storage, intent schemas, and discovery stack

Research date: 2026-07-26

## Decision

The first production slice should use a deliberately small stack:

| Concern | Shipped implementation | Version / source | Decision |
| --- | --- | --- | --- |
| Windows sandbox launcher | Windows LPAC, extended process attributes, and Job Objects | Windows API via `Microsoft.Windows.CsWin32` 0.3.298 | Use for the local Windows execution tier. Keep all native code in one Windows-only launcher assembly. |
| Worker IPC | Kestrel HTTP/2 over Windows named pipes | .NET 10 `Microsoft.AspNetCore.App` shared framework | Use bounded Kestrel transport buffers, an exact pipe DACL, and a one-use execution credential. Do not invent a binary framing protocol. |
| Artifact storage | Azure Blob Storage | `Azure.Storage.Blobs` 12.29.1 and `Aspire.Azure.Storage.Blobs` 13.4.6 | Store immutable content-addressed artifacts in a container separate from the journal. |
| Artifact identity | SHA-256 | .NET 10 `SHA256.HashData[Async]` | The digest of the exact admitted bytes is the revision identity and approval binding. Verify on upload collisions and every read. |
| Publisher provenance | COSE Sign1 detached signatures | `System.Security.Cryptography.Cose` 10.0.10 and RFC 9052 | Optional for locally created artifacts; required before a community publisher identity is trusted. A signature never grants authorization. |
| Intent validation | JSON Schema draft 2020-12 | `JsonSchema.Net` 9.3.0, subject to the license gate below | Put it behind `IIntentSchemaValidator`, restrict DigitalBrain's supported schema profile, and never resolve remote schemas. |
| Candidate discovery | Deterministic catalog scan first | DigitalBrain domain interface | Do not add a vector database in the first slice. Hundreds of modules are not a scale argument by themselves. |
| Later vector adapter | .NET vector-data abstractions | `Microsoft.Extensions.VectorData.Abstractions` 10.8.0 | Add only after a measured retrieval-quality need. Keep provider and embedding details outside the Behavior/Neuron contracts. |

This changes the implementation order:

```text
admit and test exact Behavior bytes
  -> SHA-256 identity
  -> optional publisher signature verification
  -> owner approval journal records exact digest and policy evidence
  -> create-only artifact upload
  -> deterministic catalog installation
  -> sandboxed execution from digest-verified bytes
  -> optional advisory semantic discovery projection
```

The main safety invariant is:

> Discovery proposes a candidate. The installed catalog resolves an exact revision. The owner
> approval journal authorizes it. The broker grants only declared capabilities. The sandbox
> executes it. No search result, signature, storage key, assembly name, or schema match may skip
> any of those steps.

## Repository baseline

The repository already has useful foundations, but they must not be mistaken for the new
boundaries:

- `DigitalBrain.Aspire.Hosting` creates one Azure Storage resource, an Orleans Tables projection,
  and one `journal` blob container. The Behavior artifact container must be a distinct resource
  reference and DI registration so it can have independent access and retention policy.
- `DigitalBrain.Security/DurablePayloadProtection.cs` derives a purpose-specific HMAC key and
  encrypts with AES-GCM. That protects durable secrets. It is not an artifact digest, publisher
  signature, approval proof, or executable-code sandbox.
- the repository already uses BCL SHA-256 for fingerprints. Reuse the BCL primitive, but introduce
  a strongly typed Behavior artifact digest so unrelated fingerprints cannot be confused.
- `Microsoft.Extensions.AI.Abstractions` 10.8.0 is present. No vector-store abstraction or provider
  is present, and no production code currently performs embedding search.
- there is no Blob Storage client integration, Windows interop generator, JSON Schema evaluator,
  or COSE dependency today.

The desired module ecosystem does not justify merging these concerns. Artifact storage,
authorization, discovery, validation, and execution must remain independently replaceable deep
modules.

## Windows execution boundary

### Use LPAC plus a Job Object, not `AssemblyLoadContext`

An AppContainer token carries a package SID and capability SIDs. Windows grants access as the
intersection of the normal user/group access and the AppContainer side of the DACL. AppContainers
run at low integrity and are isolated from other processes, windows, devices, files, registry,
network, and credentials unless access is granted. A Less Privileged AppContainer (LPAC) also
opts out of access granted through `ALL_APPLICATION_PACKAGES`, so it is the correct default for a
worker that should see nothing except explicitly brokered resources.
([Microsoft: launch an AppContainer](https://learn.microsoft.com/en-us/windows/win32/secauthz/implementing-an-appcontainer),
[token information classes](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-token_information_class))

`AssemblyLoadContext` may still resolve an admitted Behavior assembly inside the worker, but it
provides dependency identity and unloading, not hostile-code isolation. The security boundary is
the operating-system process.

Construct the worker with a single `CreateProcessW` call using `STARTUPINFOEX`. Populate the
attribute list before process creation with:

1. `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES`, containing the exact profile SID and no ambient
   capability SIDs;
2. `PROC_THREAD_ATTRIBUTE_ALL_APPLICATION_PACKAGES_POLICY` with
   `PROCESS_CREATION_ALL_APPLICATION_PACKAGES_OPT_OUT`, which creates the LPAC behavior;
3. `PROC_THREAD_ATTRIBUTE_JOB_LIST`, assigning the process to the prepared Job Object atomically;
4. `PROC_THREAD_ATTRIBUTE_CHILD_PROCESS_POLICY` with
   `PROCESS_CREATION_CHILD_PROCESS_RESTRICTED`;
5. a compatibility-proven `PROC_THREAD_ATTRIBUTE_MITIGATION_POLICY`.

Assigning the job through the process attribute closes the start-before-`AssignProcessToJobObject`
escape window. Windows documents the job-list and child-process attributes on
[`UpdateProcThreadAttribute`](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute).

Configure the job with:

- `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`;
- `JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 1`;
- bounded process and job memory;
- a CPU-rate hard cap appropriate for the host;
- no breakaway flags.

The broker owns a safe job handle for the entire execution and calls `TerminateJobObject` on
deadline, cancellation, protocol violation, or broker shutdown. Child-process restriction and
the active-process limit are intentional defense in depth.
([Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects),
[`JOBOBJECT_BASIC_LIMIT_INFORMATION`](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_basic_limit_information))

### Do not blindly enable every mitigation

Mitigation policy is fixed before the process starts. A baseline prototype should test DEP,
SEHOP, heap termination, bottom-up/high-entropy ASLR, strict handle checks, extension-point
disablement, font disablement, remote-image blocking, System32 preference, and Win32k disablement.

Do not enable these without proving that the exact self-contained .NET worker still starts and
loads an admitted assembly:

- dynamic-code prohibition conflicts with normal managed JIT and dynamic assembly loading;
- Microsoft-signed-binary-only policies can reject the DigitalBrain worker and dependencies;
- strict CFG or CET combinations may be incompatible with a particular runtime and native
  dependency set.

The implementation plan must include a mitigation compatibility matrix. A policy bit is not
security if it silently forces the launcher to turn off the entire sandbox.

### Use CsWin32 rather than handwritten P/Invoke

`Microsoft.Windows.CsWin32` is a Microsoft source generator over Windows metadata. Its
`NativeMethods.txt` allowlist generates architecture-appropriate signatures, supporting types,
friendly overloads, and safe handles without a runtime dependency. Add it as
`PrivateAssets="all"` to one Windows-only launcher project.
([CsWin32 getting started](https://microsoft.github.io/CsWin32/docs/getting-started.html),
[features](https://microsoft.github.io/CsWin32/docs/features.html),
[NuGet 0.3.298](https://www.nuget.org/packages/Microsoft.Windows.CsWin32/0.3.298))

The initial allowlist should contain only the APIs actually needed, including the profile,
process-attribute, process, job, token-inspection, and SID-release functions. Generated handles
must own their resources; do not spread `nint`, raw SID pointers, unions, or `unsafe` code through
the host.

This is still a prototype gate, not permission to assume generator coverage. Compile and execute
the real launcher on every supported Windows architecture and verify each generated constant,
union, and ownership rule.

### Publish the worker self-contained

LPAC intentionally cannot perform broad registry or filesystem discovery. A framework-dependent
worker may need to find the system `dotnet` host, installed runtime, or files not accessible to its
SID. Publish a self-contained `win-x64` worker for the first supported platform. NativeAOT is not
appropriate because the worker must load an approved Behavior assembly at runtime.

A provisional profile strategy is one AppContainer profile per installed
`(OwnerId, BehaviorId, RevisionDigest)`. Use a non-PII, fixed-length hash as the moniker (the
profile API limits it to 64 characters), place or ACL only the worker and exact immutable revision
to that SID, and remove the profile at uninstall.
[`CreateAppContainerProfile`](https://learn.microsoft.com/en-us/windows/win32/api/userenv/nf-userenv-createappcontainerprofile)
creates per-user profile directories and registry state.

Grant that SID read/execute access to the worker and exact installed revision, never write access.
Only the execution's bounded temporary/profile area is writable. Stage content into a new
directory, verify it, apply the final ACL, and atomically expose it; a Behavior must not be able to
replace bytes that a later execution will load under the same revision digest.

That strategy must be measured before it becomes architecture:

- compare profile-per-revision, profile-per-execution, and an ACL'd shared worker directory;
- measure creation, cleanup, installation, and cold-start cost;
- if a SID is reused, initially serialize executions for that revision and wipe its writable
  profile area so concurrent runs cannot read each other's temporary data.

### Prove the boundary at runtime

The worker should report its token evidence to the trusted broker. The broker verifies
`TokenIsAppContainer`, `TokenAppContainerSid`, `TokenIntegrityLevel`,
`TokenIsLessPrivilegedAppContainer`, and the capability list before sending any Behavior input.

Automated negative tests must prove that the launched worker cannot:

- open an arbitrary parent-profile file or registry key;
- access the network;
- spawn a child process;
- connect to another execution's pipe;
- exceed memory, CPU, deadline, or output limits;
- survive broker/job-handle termination.

Fail closed on a non-Windows host until another operating-system sandbox adapter has equivalent
tests. A mock launcher is suitable for domain tests, never for a production fallback.

## IPC and capability brokering

Windows requires AppContainer named pipes to live under the `LOCAL\` namespace. Use a high-entropy
pipe name such as `LOCAL\DigitalBrain\<random>` at the .NET level
(`\\.\pipe\LOCAL\...` in native notation). The default named-pipe DACL is too broad for this
boundary: Windows documents default access for system, administrators, creator owner, Everyone,
and anonymous identities.
([AppContainer IPC](https://learn.microsoft.com/en-us/windows/apps/develop/communication/interprocess-communication),
[named-pipe security](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights))

Use Kestrel's shipped named-pipe transport with HTTP/2. Configure
`NamedPipeTransportOptions.PipeSecurity` with an explicit DACL granting only the broker identity
and exact AppContainer SID. `CurrentUserOnly` is insufficient because it distinguishes user and
elevation level, not one Behavior worker. Set finite `MaxReadBufferSize` and
`MaxWriteBufferSize`; the API explicitly warns that zero or `null` disables backpressure and makes
unbounded buffering a security risk with untrusted clients.
([Kestrel named-pipe transport options](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.server.kestrel.transport.namedpipes.namedpipetransportoptions?view=aspnetcore-10.0),
[Microsoft gRPC named-pipe IPC example](https://learn.microsoft.com/en-us/aspnet/core/grpc/interprocess-namedpipes?view=aspnetcore-10.0))

The client connects through `NamedPipeClientStream` using
`SocketsHttpHandler.ConnectCallback`, following Microsoft's shipped IPC pattern. The shared
framework already contains the transport; no separate transport package is needed.

The protocol still needs application authorization:

- one high-entropy execution ID and one-use bearer secret delivered through inherited launch data,
  never command-line logging;
- a handshake binding execution ID, revision digest, protocol version, and nonce;
- bounded request/response sizes and stable DTO versions;
- exactly one execution per channel;
- broker-side capability allowlist and budget checks on every call;
- no Orleans client, Azure credential, storage connection, signing key, or service provider inside
  the worker.

Pipe secrecy is not enough. A DACL identifies who may connect; the one-use protocol credential
binds the connection to the expected launch.

## Hosted hostile-code threshold

Microsoft explicitly distinguishes Windows process-isolated containers from Hyper-V-isolated
containers: process isolation shares the host kernel and is not considered a robust boundary for
hostile multi-tenant code; Hyper-V isolation places each container in a lightweight VM with its
own kernel and hardware-level isolation.
([Windows container security](https://learn.microsoft.com/en-us/virtualization/windowscontainers/manage-containers/container-security),
[Hyper-V isolation](https://learn.microsoft.com/en-us/virtualization/windowscontainers/manage-containers/hyperv-container))

Therefore:

- LPAC + Job Object is the local-owner/community-code tier: it protects one user's machine from a
  Behavior they chose to install, subject to the verified Windows boundary above.
- Hyper-V isolation, a dedicated VM, or an equivalent hard multi-tenant sandbox becomes mandatory
  when DigitalBrain executes mutually hostile publishers for different tenants, holds another
  tenant's secrets on the same host, or offers hosted execution to untrusted public users.

Do not describe the LPAC tier as the hosted multi-tenant security boundary.

## Content-addressed Behavior artifacts

### Separate the artifact container from the journal

Add `Azure.Storage.Blobs` 12.29.1 to the runtime/storage adapter and
`Aspire.Azure.Storage.Blobs` 13.4.6 to the Aspire host integration. The latter provides the shipped
`AddAzureBlobClient`, `AddAzureBlobContainerClient`, and keyed variants. Preserve the existing
`journal` connection and add a stable, separately referenced Behavior-artifact container.
([Azure Blob .NET client](https://www.nuget.org/packages/Azure.Storage.Blobs/12.29.1),
[Aspire Azure Blob client integration](https://learn.microsoft.com/en-us/dotnet/aspire/storage/azure-storage-blobs-integration))

The distinction is architectural, not cosmetic:

- the journal is authoritative durable operating-system history;
- the artifact container stores immutable bytes addressed by their digest;
- each has different permissions, retention, backup, and future garbage-collection rules;
- the sandboxed worker receives neither connection.

Expose storage through a domain interface such as:

```csharp
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
```

`BehaviorArtifactLease` should expose a verified local path or stream and lifetime, not
`BlobClient`, `BlobDownloadResult`, ETags, or Azure exceptions.

### Make writes create-only and reads self-verifying

Use a canonical lowercase path such as:

```text
sha256/ab/abcdef...64-lowercase-hex
```

Hash the exact admitted artifact envelope locally. Upload with:

```csharp
new BlobUploadOptions
{
    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
}
```

Azure defines `If-None-Match: *` as succeeding only when the target resource does not exist. A
concurrent or repeated create returns HTTP 412. On 412, download the existing object and verify
its SHA-256 before treating the operation as idempotent success.
([Blob conditional headers](https://learn.microsoft.com/en-us/rest/api/storageservices/specifying-conditional-headers-for-blob-service-operations),
[`BlobRequestConditions`](https://learn.microsoft.com/en-us/dotnet/api/azure.storage.blobs.models.blobrequestconditions?view=azure-dotnet))

Verify SHA-256 on every read before a worker may load the bytes. Azure's transfer-validation
options currently offer CRC64 or MD5, not SHA-256; those can detect transport corruption but
cannot replace the artifact identity.
([`UploadTransferValidationOptions`](https://learn.microsoft.com/en-us/dotnet/api/azure.storage.uploadtransfervalidationoptions?view=azure-dotnet))

Treat Blob Storage as an untrusted byte store. The approval journal remains the authority that
binds owner, Behavior identity, exact digest, compiler/admission policy versions, test evidence,
permissions, and approval.

The artifact-envelope format itself must be versioned and deterministic. If it uses ZIP, validate
entries inside the admission sandbox before extraction: reject absolute and parent-traversal
paths, links, duplicate or case-colliding names, unexpected files, excessive entry counts,
uncompressed-size expansion, and bytes after the declared archive. Extract only into a new bounded
directory. Hash and approve the stored envelope bytes, then separately verify the admitted
manifest's hashes for every executable payload.

### Do not start with WORM or versioning

Azure immutable storage can apply time-based retention and legal holds, including version-level
WORM policies. It is useful for compliance, but it changes lifecycle and billing and can prevent
normal garbage collection.
([immutable storage](https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-storage-overview),
[version-level WORM](https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-version-level-worm-policies))

Content addressing, create-only writes, read verification, and the approval journal provide the
initial correctness invariant. Add WORM only for an explicit compliance or hostile-operator
requirement, not as a substitute for those controls.

The later artifact collector should be mark-and-sweep over journal/catalog references, with an
explicit retention window. “Immutable” means revisions are never modified; it does not mean every
unreferenced revision must be retained forever.

## Artifact digest and publisher signatures

Use .NET's static `SHA256.HashData` or `HashDataAsync` implementation. Do not create another
hashing algorithm or use the existing durable-payload encryption key to identify artifacts.
([`SHA256.HashData`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.sha256.hashdata?view=net-10.0))

If community distribution needs publisher provenance, use a detached COSE Sign1 envelope from
`System.Security.Cryptography.Cose` 10.0.10 rather than a custom signature format. COSE Sign1 is
the IETF standard single-signature structure; .NET ships `CoseSign1Message.SignDetached` and
`VerifyDetached`.
([RFC 9052](https://datatracker.ietf.org/doc/rfc9052/),
[`CoseSign1Message`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cose.cosesign1message?view=net-10.0-pp),
[NuGet 10.0.10](https://www.nuget.org/packages/System.Security.Cryptography.Cose/10.0.10))

Recommended v1 profile:

- detached signature over the exact content-addressed artifact envelope;
- ECDSA P-256 with SHA-256;
- protected algorithm and key-ID headers;
- external associated data containing the domain separator
  `DigitalBrain.BehaviorArtifact/v1`;
- a trust store mapping key ID to publisher and current trust/revocation state.

Verification order is digest, signature, then owner approval. This prevents four dangerous
equivalences:

- valid signature does not mean the owner approved execution;
- known publisher does not mean every revision is trusted;
- approved Behavior identity does not authorize a different digest;
- a digest does not say who produced it.

Locally generated owner artifacts do not need a publisher signature when the authenticated owner
approval journal already binds their exact digest. Keep key storage, rotation, revocation, and
trust policy behind `IArtifactSignatureVerifier`; no private signing key belongs in the worker or
artifact store.

## Intent JSON Schema

### Use draft 2020-12 behind a DigitalBrain profile

JSON Schema draft 2020-12 supplies the vocabulary and validation semantics needed for Behavior
intent inputs. The implementation must explicitly select that dialect and require the
`$schema` declaration; a library default is not an architecture decision.
([JSON Schema 2020-12 core](https://json-schema.org/draft/2020-12/json-schema-core),
[validation](https://json-schema.org/draft/2020-12/json-schema-validation))

`JsonSchema.Net` 9.3.0 provides draft 2020-12 support and the needed
`JsonSchema.FromText`, `Evaluate`, `BuildOptions.Dialect`, `EvaluationOptions`, and local registry
APIs. Use it only through:

```csharp
public interface IIntentSchemaValidator
{
    IntentSchemaCompilation Compile(
        IntentSchemaSource source,
        IntentSchemaPolicy policy);

    IntentValidationResult Validate(
        IntentSchemaCompilation schema,
        JsonElement intent);
}
```

Do not leak `JsonSchema`, `EvaluationResults`, registry, or keyword types into Neuron, Behavior, or
module contracts.
([JsonSchema.Net basics](https://docs.json-everything.net/schema/basics/),
[`EvaluationOptions`](https://docs.json-everything.net/api/JsonSchema.Net/EvaluationOptions/),
[`SchemaRegistry`](https://docs.json-everything.net/api/JsonSchema.Net/SchemaRegistry/))

### The current binary license needs an explicit gate

The current NuGet package is source-available under MIT, but its binary package includes the Open
Source Maintenance Fee EULA. That EULA requires payment for certain revenue-generating users.
Do not silently introduce that obligation.
([JsonSchema.Net 9.3.0 package and license](https://www.nuget.org/packages/JsonSchema.Net/9.3.0))

Before implementation, choose one of:

1. obtain legal acceptance and use the official 9.3.0 binary;
2. build the exact reviewed 9.3.0 source tag under its MIT source license in the controlled
   dependency pipeline;
3. select another validator only after proving full required 2020-12 semantics and safety against
   the official conformance suite.

Do not pin the old 7.4.0 binary merely to avoid the new EULA. Later releases fixed important
schema-cycle and identifier-order defects, including a self-referential cycle gap that could
produce a stack overflow.
([JsonSchema.Net release notes](https://docs.json-everything.net/rn-json-schema/))

### Restrict schemas as admitted executable policy

The validator can fetch remote schemas through a configured registry callback; DigitalBrain must
not configure one. Admit only self-contained schemas and same-document fragment references. The
schema becomes immutable evidence attached to a Behavior revision.

Define a versioned `DigitalBrain Intent Schema Profile v1` that:

- permits exactly draft 2020-12;
- rejects unknown keywords in a separate policy pass rather than assuming the evaluator will;
- caps source bytes, parsed depth, property count, array length, string length, total instance
  size, reference count, and combinator depth/branch count;
- disallows remote `$ref`, external resource identifiers, custom vocabularies, and runtime
  format/network resolution;
- starts without `pattern` and `patternProperties` unless a bounded validation-process prototype
  proves regex behavior safe;
- recommends or requires `additionalProperties: false` for public object envelopes.

Compile and policy-check the schema at Behavior admission time, cache it by revision digest, and
return stable DigitalBrain error DTOs at runtime. `Evaluate` has no cancellation-token overload;
if the product later permits the full language, schema construction and evaluation need their own
CPU/memory/deadline process boundary.

The conformance gate must run the official draft-2020-12
[JSON Schema Test Suite](https://github.com/json-schema-org/JSON-Schema-Test-Suite) plus
DigitalBrain adversarial cases for cycles, deep nesting, combinator explosion, pathological
patterns, oversized instances, and remote references.

## Behavior and module discovery

### Deterministic catalog first

A few hundred or even a few thousand compiled module and Behavior descriptors are small enough to
filter and rank deterministically in memory. Start with exact identifiers, aliases, declared
intent schema, owned capabilities, visibility, module version, and stable descriptive text.

The AI assistant remains able to trigger and compose Behaviors:

```text
user intent
  -> assistant extracts a bounded intent envelope
  -> catalog candidate discovery
  -> exact descriptor resolution
  -> intent-schema validation
  -> owner/visibility/permission/policy checks
  -> approved Behavior invocation or explicit composition proposal
```

“Compose its own” means proposing a new canonical Behavior source + schema + BDD evidence that
passes admission and owner approval. It never means synthesizing code and immediately running it
inside the trusted silo.

Use a provider-neutral seam:

```csharp
public interface ICatalogCandidateDiscovery
{
    ValueTask<IReadOnlyList<CatalogCandidate>> FindAsync(
        CatalogDiscoveryQuery query,
        CancellationToken cancellationToken);
}
```

Candidates contain stable catalog IDs, score, and human-readable match reasons. The caller
re-resolves each ID against the authoritative catalog and repeats owner, visibility, version,
schema, and capability checks. A discovery score is advisory data, never authorization.

### Add vectors only when a benchmark earns them

Microsoft's stable `Microsoft.Extensions.VectorData.Abstractions` provides
`VectorStore`, `VectorStoreCollection<TKey,TRecord>`, CRUD, vector search, filters, hybrid search,
and embedding-generator integration. Keep those types inside a later adapter.
([MEVD overview](https://learn.microsoft.com/en-us/dotnet/ai/conceptual/mevd-library),
[vector-store overview](https://learn.microsoft.com/en-us/dotnet/ai/vector-stores/overview),
[usage](https://learn.microsoft.com/en-us/dotnet/ai/vector-stores/how-to/use-vector-stores),
[NuGet 10.8.0](https://www.nuget.org/packages/Microsoft.Extensions.VectorData.Abstractions/10.8.0))

Do not add a provider now. The current Microsoft Semantic Kernel connector packages, including
InMemory, Azure AI Search, and Qdrant, are preview packages and currently depend on older
Microsoft.Extensions.VectorData/AI versions than the repository's 10.8 line. Microsoft's docs
also describe the in-memory provider as a prototype option and advise using the production
database, commonly through test containers, for realistic tests.
([InMemory connector 1.74.0-preview](https://www.nuget.org/packages/Microsoft.SemanticKernel.Connectors.InMemory/1.74.0-preview),
[Azure AI Search connector 1.74.0-preview](https://www.nuget.org/packages/Microsoft.SemanticKernel.Connectors.AzureAISearch/1.74.0-preview),
[Qdrant connector 1.74.0-preview](https://www.nuget.org/packages/Microsoft.SemanticKernel.Connectors.Qdrant/1.74.0-preview))

The adoption benchmark must compare exact catalog scanning with a real candidate provider at
100, 1,000, and 10,000 descriptors:

- recall@k against a reviewed intent set;
- p50/p95/p99 query latency and index/update cost;
- owner and visibility filter isolation;
- deterministic reindex/rebuild behavior;
- results after embedding-model upgrade;
- provider parity in production-like integration tests.

If exact scanning meets the service-level objective and retrieval quality, there is no reason to
operate a vector database.

When an adapter is added:

- the embedding model, version, vector dimension, normalization, chunk/projection policy, and
  catalog schema version form an immutable index-generation ID;
- changing any of them creates a rebuild, not an in-place semantic mutation;
- the index is a disposable projection of the installed catalog;
- owner/visibility filtering occurs provider-side to limit semantic leakage and is repeated after
  retrieval;
- exact descriptor resolution remains mandatory before invocation.

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
