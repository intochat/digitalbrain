# S1.6 — Gmail strangler: typed path → official Gmail MCP   (role: GREEN)

Report path: `plans/stage1/reports/S16-gmail.md`

## Ratified sequence (binding — §1.14; steps 1–2 already landed in S1.3)
3. Connect the official Google Gmail MCP server through the generic MCP gateway.
4. The same per-user isolation + OAuth suite passes against the Gmail server key.
5. Only then: DELETE the typed Gmail path. Parity first, deletion second.

## Objective
1. **Gmail as an MCP server definition**: register a `McpServerDefinition` for Gmail the same
   way Salesforce is registered (server key e.g. `google/gmail`), endpoint + OAuth parameters
   from configuration (Developer Preview endpoint is config, never hardcoded reachability in
   tests). Per-user OAuth flows through the S1.3 rail (PKCE, principal-bound state,
   principal-keyed Integration tokens) with NO Gmail-specific code beyond the definition +
   OAuth parameter set (that is what the SDK rails exist for).
2. **Parity proof**: run the S1.3 fake-provider suite shape against the Gmail server key —
   begin/callback/one-shot/replay/expiry, per-principal token isolation (user A never reaches
   user B's Gmail), tool call journal audit with actor+subject. Reuse/parametrize the existing
   proofs rather than cloning.
3. **Delete the typed Gmail path** (only after parity is green): `GmailAuthRail`, the typed
   Gmail planner/API surface, `DurableGoogleTokenStore`, Gmail-typed contracts nothing else
   consumes, and the `Google.Apis.Gmail.v1` (+`Google.Apis.Auth` if now unused) package pins
   from `Directory.Packages.props` — package REMOVAL is explicitly granted here (never
   additions). If the Google module ends up empty, delete the empty project(s) from the
   solution like any dead code; if OAuth parameter types remain, keep the module minimal.
4. **No orphaned references**: assistant/chat/tools flows that referenced typed Gmail
   contracts now discover Gmail via the live MCP catalog (`db.mcp.list-tools`) — verify
   `find_capabilities`/manifest reflection sees no dead Gmail contracts. Search the whole
   solution incl. Scripting samples and MCP ChatTools.
5. **Live-endpoint honesty**: real Google connectivity is NOT testable headless — the report
   must state exactly what is proven in-process (definition, rail, isolation, catalog) vs
   what the Stage-1 exit smoke must verify live (sign-in against real Google, listed tools).

## Constraints
Follow the Salesforce module as the reference shape for a provider = definition + capability +
OAuth params. TDD. Kernel traps (esp. 2: no facts emitted into removed routes; 6: manifests
are reflected — deleting contracts changes catalogs, confirm no ghost handlers remain). Wire
aliases: typed `gmail.*`/google aliases being deleted is expected — confirm no real data
depends on them (this installation is pre-production; delete-and-reauthorize stance recorded).
No git.

## Definition of done
Gate green; parity proofs green against the Gmail server key; zero typed-Gmail references in
src/; packages removed from props; report includes the in-process vs live-smoke table.
