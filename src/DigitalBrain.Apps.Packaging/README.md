# Compiled app packages

Internal, non-publishable framework library. `.intochat-app` is a ZIP with schema
1, host ABI 1, target `net11.0` and a canonical UTF-8 `app.json`. Payload names use
forward slashes. `FrameworkCompatibility = "1"` names the compiled framework
host contract ABI, not the framework product version.

`CompiledAppDescriptor` freezes identity, display name, version, entry assembly
and type, capability IDs, required host adapters, presentation, contracts,
signals, owned neurons and SHA-256 payload inventory. Every inventory row has
`path`, uncompressed `length` and lowercase `sha256`. `sharedAssemblies` maps
simple assembly names to exact assembly versions; the receiving host supplies
its compatible map to `AppPackageReader`. Shared DLLs are omitted by the writer
and cannot also appear in the private payload.

Neuron metadata contains the CLR type name, frozen neuron alias, persisted state
name, `affine`/`reentrant`/`stateless` policy and optional state contract. State
contracts and signals have an explicit serializer alias, schema version and
fields (stable ID, JSON member name, portable type, requiredness and optional
JSON default). A null state denotes no declared persisted state. CLR type names
never contain assembly-qualified generic arguments. Legacy ownership is an
explicit list of `exact-key` or `key-prefix` claims; the host must authorize
those claims for its destination rather than infer ownership from aliases.

`hostMetadata` is an ordinal map of extension names to opaque strings, normally
canonical host-specific JSON. Reserve `intochat.os.v1` for OS operations,
settings, slots, toolbar and semantic, secret and connection requirements. The library
preserves those bytes without referencing OS assemblies. The host must validate
the extension and reconcile it with the typed framework manifest after owner
consent. An extension never grants or widens framework capabilities.

`TrustedBuildMetadata.Extract(entryType, manifest, hostMetadata)` is the local
build-time seam for a template/MSBuild helper or CLI. It inspects trusted loaded
developer types and may execute their default constructors/property getters when
recording defaults. Contracts need explicit frozen serializer aliases and field
IDs; ambiguous metadata fails instead of guessing a storage identity. It checks
the `[App]` identity and preserves explicit host extensions while deriving
framework identity, version and capabilities from the typed manifest. Receiving
code only uses `AppPackageReader`, which never loads an assembly.

The writer sorts entries/metadata, fixes ZIP timestamps and attributes, and uses
stored payloads for deterministic output within the same runtime/platform. It
omits source, symbols, common secret/configuration files and intermediates.
Only point it at a dedicated Release delivery directory: arbitrary asset content
cannot be classified as a secret by filename.

The reader hashes and bounds the archive while copying, then validates names,
entry counts, symlinks, metadata, ABI, inventory, hashes, managed PE headers and
native imports. Defaults: 128 MiB archive, 256 MiB expanded including metadata,
4,096 entries including metadata and 1 MiB metadata. It writes a private
candidate, marks accepted files read-only, and atomically promotes to
`stagingRoot/<archive-sha256>`. Reuse verifies existing bytes and never replaces
an artifact. Rejection/cancellation removes private candidate content. The
configured staging boundary must be host-owned. Read-only attributes discourage
accidental writes; they do not sandbox owner-approved in-process code. Artifact
hashes identify bytes and do not authenticate a publisher.
