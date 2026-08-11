# S1.2-GREEN-2b — close GRILL MAJORs 1+3 (+3 minors)   (role: GREEN, surgical)

Report path: `plans/stage1/reports/S12-identity-green2b.md`

The GRILL report (`plans/stage1/reports/S12-grill.md`) approved the identity seam with findings.
The orchestrator ruled findings 1 and 3 in-seam (must fix now) and 7, 8, 9 cheap enough to take.
Fix EXACTLY these, nothing else:

1. **Finding 1 (MAJOR) — surfaces must be principal-scoped like chats.** An authenticated user
   currently opens/watches another user's surface by client-supplied name
   (`MapShellStreams.cs` surface path, `MapOwnerCommands.cs` surface.open). Apply the same
   pattern GREEN-2 used for chats (`PrincipalChat`): surface instance identity derives from the
   caller's principal; the client name only selects among the caller's own surfaces. Extend the
   existing principal-scoping tests to surfaces.
2. **Finding 3 (MAJOR) — enforce the ratified HTTPS stance in-process.** Add one small middleware:
   a request that is NOT loopback and NOT secure (no https scheme and no `X-Forwarded-Proto:
   https` from a fronting proxy) is refused before auth runs (403 with a one-line reason). No
   config switch to disable it — the ratified rule is unconditional beyond localhost. Loopback
   behavior unchanged. Test both directions.
3. **Finding 7 (MINOR)**: on login, honor `PasswordVerificationResult.SuccessRehashNeeded` —
   rehash and store.
4. **Finding 8 (MINOR)**: a null remote IP is NOT loopback — dev bypass and the HTTPS stance
   must treat it as remote.
5. **Finding 9 (MINOR)**: malformed conversation names / broken auth claims on the command and
   stream endpoints return 400/401 (as fits), never 500. Cover with tests.

## Constraints
Smallest honest diff; follow the existing Auth/ patterns; no new packages; no contract changes
except what surface scoping strictly requires; do not touch OAuth, MCP, workspace grains, or the
chat pipeline beyond surface scoping; TDD; full gate before the report; no git.

## Definition of done
Gate green; new/extended tests prove: cross-principal surface access impossible by name,
non-loopback plain-HTTP refused, loopback flows unchanged, rehash-on-login, null-IP-is-remote,
no 500s on malformed input. Report per GROK.md format.
