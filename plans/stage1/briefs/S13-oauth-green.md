# S1.3-GREEN — replace the OAuth/Integration rail   (role: GREEN)

Report path: `plans/stage1/reports/S13-oauth-green.md`

## Ratified constraints (binding — RATIFIED-PRODUCT-DEFINITION.md §1.14, §3 P0-1/P0-5/P0-7)
- ONE PKCE authorization flow for all providers; the manual non-PKCE URL path dies.
- Authorization state is bounded, expiring, one-shot, and bound to workspace + authenticated
  user principal + credential subject. Completed codes are never replayable.
- Integration record: `{Provider, Scope: User|Workspace, SubjectId, ExternalAccount,
  GrantedScopes, ProtectedTokenReference}`. Tokens keyed by verified principal — never by bare
  neuron/server name. Strictly per-user resolution in ordinary chat; NO silent fallback to
  workspace or other users' credentials.
- ALL MCP tools allowed (ratified): remove the destructive-tool blanket rejection. Keep the
  generic invariants: never cross user integration boundaries; audit actor + integration + tool
  + correlation + outcome in journals; never log tokens.
- Unauthorized tool calls refuse settled (`NeuronAuthorizationException`) with the sign-in fix
  path in the message (existing pattern) — refusals must remain loud and correctable.

## Objective
1. **Kill the dual state (P0-1)**: one state minted per authorization transaction, PKCE
   (S256 code challenge) always, bound server-side to `{workspace, principal, provider,
   server key}`; delete the parallel manual-URL path.
2. **Bound the state store (P0-1)**: pending authorization transactions live in a durable,
   capacity-bounded, expiring store (use existing kernel state mechanics — a neuron owns them;
   no static dictionaries). Expired or unknown states refuse; a consumed state refuses on
   second presentation (one-shot); pin all three.
3. **Bind callbacks to the principal (P0-5)**: `/oauth/callback` correlates the state to the
   originating principal's transaction; a callback for a state minted by another principal (or
   none) refuses. The callback endpoint stays anonymous at HTTP level (provider redirect) but
   completion resolves ONLY through the state binding.
4. **Key tokens by principal (P0-5)**: token storage moves from neuron/server-name keying to
   the Integration record shape above, protected via the existing `DigitalBrain.Security`
   envelope mechanics. Chat-path MCP calls resolve integrations strictly for the calling
   principal (the actor stamp from S1.2 flows through `db.mcp.*` — extend those contracts with
   the actor where needed).
5. **Allow all tools (P0-7)**: remove the destructive-tool rejection; keep + extend the audit
   journaling invariants above.
6. **Flip the RED pins** for P0-1/P0-5/P0-7 (assert new behavior, remove markers). RED's report
   (`plans/stage1/reports/S13-oauth-red.md`) lists the pins and untestable spots.

## Migration honesty
Existing stored tokens (old keying) do NOT need migration code — this installation is
pre-production; delete-and-reauthorize is the ratified stance for Stage 1. State this in the
report so it's an explicit decision, not an accident.

## Design discipline
Study first: `McpAuthorizationRail`, `McpClientSessions`, `McpAuthorizationNeuron`,
`AuthorizationFacts`, the OAuth callback map, `DigitalBrain.Security` envelope APIs, and how
S1.2 threads `ActorContext`. Follow kernel patterns (settled refusals, journal-is-outbox,
reflected manifests). Fake-provider tests in-process; never call real providers; never run
aspire. Wire aliases: `db.mcp.*` stay; additive fields fine. No new packages. TDD. No git.

## Out of scope
Gmail typed-path deletion (S1.6), chat turn pipeline (S1.5), Execution (S1.4), Flutter, CI.

## Definition of done
Gate green; pins flipped; fake-provider e2e proves: PKCE on every URL, one-shot expiring
principal-bound states, replay refused, per-principal token isolation (user A can never reach
user B's Salesforce), destructive tool callable by an authorized principal, audit journal
entries carry actor+integration+tool+outcome. Report with design note.
