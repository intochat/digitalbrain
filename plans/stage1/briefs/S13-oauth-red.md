# S1.3-RED — characterize the OAuth/MCP authorization rail   (role: RED)

Report path: `plans/stage1/reports/S13-oauth-red.md`

## Objective
Pin TODAY'S OAuth/state behavior with tests so the rail replacement changes it consciously.
Tests only; no production behavior change (test seams like `InternalsVisibleTo` allowed).

## What to pin (defect pins carry `// PIN-DEFECT(P0-x): <line>`)
1. **P0-1 dual state**: the MCP authorization path mints its own state/URL (around
   `McpAuthorizationRail`) while the MCP client library mints a different state later (around
   `McpClientSessions`). Pin both existences and that they are NOT the same value/flow.
2. **P0-1 no PKCE on the manual URL**: pin that the manually-built authorization URL carries no
   `code_challenge`.
3. **P0-1 unbounded callback state**: locate the static/instance dictionary holding pending
   callback states; pin that unknown states accumulate (no expiry/eviction) and that a
   completed state/code can be presented again (replay) without refusal.
4. **P0-5 token keying**: pin that MCP/Gmail tokens are stored keyed by neuron/server identity
   (e.g. server key like `dev/salesforce` or durable neuron name), NOT by any verified user
   principal. Point at the exact storage record shape.
5. **P0-7 destructive-tool rejection**: pin that `db.mcp.call-tool` refuses destructive tools
   without approval today (this pin gets REMOVED when the ratified allow-all lands in GREEN).
6. **OAuth callback binding**: pin that `/oauth/callback` handling does not bind the state to
   any local user identity (the new Host auth from S1.2 is now in the tree — callback still
   ignores the authenticated principal).

## Method
Follow existing test conventions (NeuronTest/DigitalBrainTest styles). Fake in-process
provider/HTTP where the existing tests already fake; do NOT call real providers, do NOT run
aspire. Where a spot is untestable without production refactor, STOP on that item and record it
precisely (file:line, why) in the report — the GREEN brief will handle it.

## Out of scope
Any production behavior change, the S1.2 auth code, new packages, git.

## Definition of done
Gate green (build 0 warnings + full test exe pass); every pin listed in the report with test
name; untestable items documented with exact locations.
