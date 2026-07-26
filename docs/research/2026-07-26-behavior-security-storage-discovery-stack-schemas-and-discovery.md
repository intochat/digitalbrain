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
