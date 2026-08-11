# S1.2-RED — characterize the identity boundary before replacing it   (role: RED)

Report path: `plans/stage1/reports/S12-identity-red.md`

## Objective
Pin TODAY'S identity behavior with tests, so the identity seam replacement (next session) changes
behavior consciously, never silently. You add tests and test-only seams. You change NO production
behavior.

## What to pin (each pin that documents a defect carries `// PIN-DEFECT(P0-x): <line>`)

1. **P0-3 singleton owner**: the host composes exactly one `IDigitalBrain` for owner `"dev"`.
   Pin where that constant lives and that every session/chat resolves under it.
2. **P0-4 client-trusted chat identity**: the owner-command path accepts a client-supplied chat
   name and routes to that chat grain — two different "users" (two clients) sending with the same
   chatName share one transcript. Pin at the command-handling seam (grain/handler level is fine;
   full HTTP hosting tests are NOT required if the handler seam is testable).
3. **Unauthenticated surface**: enumerate the HTTP endpoints the Kernel maps (owner/commands,
   chats events, surfaces events, brain/topology, graph/events, authorizations/events, oauth
   callback) in a characterization test or, where endpoint-level testing is impractical, a pinned
   inventory test over the route registration code. Document in the report exactly which
   endpoints exist and that no authentication middleware is registered (P0-3).
4. **Actor absence**: pin that durable chat commands / journal facts today carry NO actor
   identity (this is what the seam will add).

## Method
- Tests go in `src/Tests/DigitalBrain.Tests`, NeuronTest-style or DigitalBrainTest-style,
  following the existing fixture patterns (`InProcessTestClusterBuilder`, volatile journal
  storage, in-memory reminders). Study neighboring tests first and match their conventions.
- If a seam is untestable without a small production refactor (e.g. extracting a pure function),
  prefer `InternalsVisibleTo`/internal access over public API changes; if even that requires a
  production change, STOP on that item and record it in the report instead.
- Run the full gate before writing your report.

## Out of scope
Any production behavior change. Any auth implementation. Any new packages. Any git.

## Definition of done
Gate passes; new pins are green; report lists every pin with its test name and every endpoint in
the HTTP surface inventory.
