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
