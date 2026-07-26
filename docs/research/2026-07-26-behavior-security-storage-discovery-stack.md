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

## Evidence map

- [Sandbox and IPC](./2026-07-26-behavior-security-storage-discovery-stack-sandbox-and-ipc.md)
- [Artifact storage and signatures](./2026-07-26-behavior-security-storage-discovery-stack-artifact-storage-and-signatures.md)
- [Schemas and discovery](./2026-07-26-behavior-security-storage-discovery-stack-schemas-and-discovery.md)
- [Public seams and proof spikes](./2026-07-26-behavior-security-storage-discovery-stack-public-seams-and-proof-spikes.md)
