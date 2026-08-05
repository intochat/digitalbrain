# Scenario 42: Collaborative: share a pane projection without sharing journals

## User intent
Ada wants Beau (another person) to see a live sales dashboard pane from Ada’s brain during a meeting—charts and headline metrics only—without granting Beau access to Ada’s journals, raw emails, or the ability to invoke Ada’s capabilities.

## Trigger
Ada UI action `ShareProjection(paneId, recipient, ttl)` from shell.

## Imagined modules
- Projection / share gateway
- Dashboard builder (Ada’s context)
- Guest edge session for Beau
- Policy / redaction
- UiProjector (guest mode)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| Dashboard / ada:sales | Emits UiSurface metrics in Ada brain |
| ShareGateway / ada | Mints projection tokens; redacts |
| GuestSession / beau:view-88 | Read-only surface consumer |
| PolicyRedactor / ada | Strips sensitive blocks |
| Audit / ada | Journals ShareGranted/Revoked |
| Beau shell | Renders guest surfaces only |

## Synapse choreography
1. Ada `ShareProjection` → ShareGateway Asks PolicyRedactor for allowlist of surface block types.
2. ShareGateway Emits `ProjectionShareCreated(token, ttl)` and configures guest route.
3. Dashboard continues to broadcast `UiSurface` in Ada context; ShareGateway overhears/filters and Emits `GuestUiSurface` into guest session stream (derived facts, not journal replay).
4. Beau’s GuestSession hears only `GuestUiSurface`; any Ask from Beau to Ada neurons is `ConnectionRefused`.
5. TTL reminder fires `ProjectionShareExpired`; disconnect guest bindings.
6. Ada can `RevokeShare` immediately; audit trail in Ada’s journal only.
7. Beau never receives JournalSlice APIs.

## Orleans / Core surface exercised
Pub-sub/streams for guest surfaces; request context (guest vs owner); grain call filters; DurableGrain journals (Ada audit only); placement; watchers for surface fan-out; Connect/Disconnect for share topology.

## Rich experience
Ada: “Sharing” badge on pane with who/ttl; Beau: branded read-only dashboard, no chat tools, watermark; revoke instantly removes Beau’s feed.

## Failure / adversarial cases
- Guest tries replay API / MCP journal tools → denied at edge.
- Surface accidentally includes email snippets → redactor must drop unknown/PII blocks by default.
- Token leak after expiry → token epoch invalidation.
- Guest share used as confused deputy to trigger Ada Ask → filters block.

## Capability claim
DigitalBrain can share *projections* as first-class derived facts without sharing the owner’s journaled nervous system—collaboration without co-tenancy of memory.
