# V2 Runtime ADRs

* V2 is a fresh authoritative runtime. It uses `v2:<tenant>:<workspace>:...` grain identities and never reads or rewrites V1 journals, credentials, feeds, or state.
* `RequestContext` is the only authority source. Client IDs, connection IDs, workspace request fields, MCP arguments, and free-form approver names are routing/input data and cannot grant access.
* `AggregateCommit` seals immutable V2 events; `OutboxRecord` is intent only and `EffectTransitionRecord` is append-only history. Unknown non-idempotent outcomes remain explicit and are never blindly retried.
* HTTP MCP is authenticated and mutation tools are disabled in Production unless an explicit V2 capability flag is enabled. Trusted stdio remains Development-only.
* V2 redaction is centralized in `V2Redaction`; secret values are represented by references or `[REDACTED]`, never by durable event payloads.
* V2 private feeds are workspace-scoped, monotonically sequenced, resumable, acknowledged, and retention-bounded. A cursor older than retention produces a reset rather than silently skipping data.
* UI action bindings hash issued tokens, enforce expiry and binding-wide usage limits, and allocate operation/idempotency identifiers before downstream submission.
* Model selection is policy-driven and fails closed across privacy, residency, health, capability, cost, latency, and token constraints; fallback never crosses policy boundaries.
* Metrics use an allow-list of low-cardinality labels. Tenant/workspace/operation identifiers are trace context only, and telemetry queues expose drop accounting.
* Deployment previews are pure comparisons. Profile, required-resource, kind, and immutable image-digest drift are blocking and never trigger a cloud mutation.
* V2 operator bootstrap consumes a configured secret once, creates an operator-scoped short-lived session, and is disabled unless `brain.admin` policy explicitly allows it. gRPC V2 sessions require signed metadata and an exact audience.
* Signed V2 session claims include principal kind and authentication assurance; validation never upgrades or downgrades these claims from transport defaults.
