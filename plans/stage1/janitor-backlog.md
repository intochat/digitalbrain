# Stage 1 janitor backlog

This is the source-of-truth backlog after the 2026-08-11 owner amendments. Historical test
hardening and flake tickets are closed as archaeology: automated tests are intentionally deferred
until final hardening, where each module will own its own test project/framework.

## Carried into Stage 2

- **J4 — duplicated module catalogs:** AppHost `AddModule<T>` and
  `DigitalBrainComposition.ComposedModules` remain two composition lists. Consolidation is not a
  mechanical Stage-1 cleanup; Stage 2 owns the module boundary decision.
- **J5 — Orleans Streams/PubSub provisioning:** precise production-source search finds provisioning
  only and no `GetStreamProvider`, `IAsyncStream`, `SubscribeAsync`, or implicit stream consumer.
  Keep it through the Stage-1 AppHost smoke, then remove the unused queue/table rail in Stage 2.
  Outbox traffic must never move onto Streams.
- **Identity hardening:** make bootstrap/user-create atomic with storage ETags; retain admin-create
  as the current invitation MVP and design email invitations in Stage 2.
- **Northbound MCP boundary:** authenticate the HTTP MCP surface and principal-scope Chat tools;
  owner-only introspection must not expose another user's private chat. This is a Stage-1 exit
  blocker until resolved, not silently accepted debt.
- **Installation owner key:** replace/configure the internal `"dev"` owner during Stage-2
  composition consolidation. It is the one-workspace deployment key, never a caller identity.
- **S1.5 riding debt:** POST observer starts at `afterSequence: 0`; MAF tool effects retain the
  post-effect/pre-safe-point uncertainty window; fire-and-return worker execution remains a
  concurrency residual. Live workers now renew an attributed Execution lease, so slow calls are
  not falsely abandoned at the 15-second liveness deadline.
- **S1.3 riding debt:** add structured failure audit outcomes without token material; revisit pin
  strength when module-owned tests are designed.
- **Salesforce operator configuration:** register the exact local callback
  `http://localhost:5080/oauth/callback` in the Salesforce External Client App. The Stage-1 live
  authorize request currently returns `redirect_uri_mismatch`; code-side callback composition is
  already canonical.

## Resolved or deliberately kept

- **J1 resolved:** keyword `WantsTimeButton`/`ShowTime` behavior and its transform are deleted;
  reply author stamps are present; production Flutter gallery ids are neutral.
- **J2 deliberately kept by owner:** `DigitalBrain.Modules.Salesforce.Contracts`, its Salesforce
  project reference, and its solution entry are permanent neuron/synapse contract boundaries.
- **J3 resolved:** stale `DigitalBrain__Modules__0..9` compose variables and Docker comments are gone.
- **J8 resolved:** `bin/`, `obj/`, `.vs/`, `.codegraph/`, and `.grok/` are ignored.
- **Deleted-suite residue resolved:** central-test `InternalsVisibleTo` grants, OAuth hub test reset/
  counter methods, the chat fault-injection branch, and fake `"worker"` allow-list seed are gone.
- **Token presence resolved:** the typed Gmail path is gone; `McpTokenPresence` is now the one shared
  SDK implementation used by Salesforce and Gmail MCP integrations.
- **Authorization-stream isolation resolved:** every authorization fact is projected only to its
  stamped principal; actorless historical facts are not exposed.
- **Orleans dashboard deliberately kept:** `/orleans` is covered by the Kernel fallback auth policy
  and is workspace-wide for the one-workspace MVP. Finer operator roles can follow in Stage 2.
- **Webhook slice deliberately kept:** it is the ratified SDK webhook-ingress rail seed (§1.18).
- **Behavior Studio fixtures deliberately kept:** visibly labelled preview data until Stage 3
  provides the real Behavior host.

## Final hardening

- Design module-owned automated-test projects/frameworks after seams stabilize. Never restore one
  repository-wide test project.
