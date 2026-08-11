# S1.2-GRILL — adversarial review of the identity seam   (role: GRILL)

Report path: `plans/stage1/reports/S12-grill.md`

You are a hostile reviewer with NO stake in the change. The uncommitted working tree contains
part 2 (Host auth boundary) of the identity seam; part 1 (workspace grains) and the RED pins are
already committed (see `git log -3`). Review the WHOLE seam: `git diff HEAD~2` = RED + GREEN-1 +
GREEN-2 combined; `git diff` = the uncommitted GREEN-2.

## Attack list (in order)
1. **Ratified conformance** (`plans/RATIFIED-PRODUCT-DEFINITION.md` §1.13):
   credentials/sessions ONLY at Host (grep the grains for password/hash/secret material —
   any hit is a BLOCKER); membership/roles/invitations/audit in grains; roles exactly
   Owner/Admin/Builder/Viewer; last-Owner invariant; HTTPS-beyond-localhost stance;
   loopback bypass ONLY in Development.
2. **Security of the auth implementation**: password hashing algorithm (must be the framework
   `PasswordHasher`, not homemade); cookie flags (HttpOnly, Secure policy, SameSite); login
   endpoint not enumerating users (same failure for unknown user vs wrong password); bootstrap
   endpoint refuses after first Owner; no secrets or hashes in logs/journals; the table-storage
   user store never stores plaintext.
3. **Actor propagation truth**: find the durable command record — does it REALLY persist the
   actor stamp (survives via journal), or only flow it in-memory via RequestContext (which the
   ratified definition explicitly rejects)? Follow one message end-to-end in code:
   login → POST /owner/commands → chat grain → journal → Responded.Author.
4. **P0-4 really dead?** Can any request path still reach ANOTHER principal's chat/transcript
   by supplying a name? Check every endpoint that takes a name (`/chats/{name}/events`,
   surfaces, commands). SSE endpoints must enforce the same isolation.
5. **Kernel traps**: trap 3 (any new grain interface crossing neurons — is it reified or
   correctly listed in `FrameworkInterfaces`?); trap 4 (refusals settled via
   `NeuronAuthorizationException`, not retried); trap 8 (did any new `IHandle<T>` join the
   broadcast catalog unintentionally?); trap 2 (zero-receiver emissions on new emits).
6. **Pins**: RED's P0-3/P0-4 pins flipped with markers removed? Any pin deleted instead of
   flipped (deletion = hiding evidence = BLOCKER)?
7. **Quality**: single-responsibility of new types, no dead code, no copy-paste between store
   and grains, naming matches kernel vocabulary, tests actually assert behavior (not just
   "doesn't throw").
8. Verify the gate yourself: `dotnet build DigitalBrain.slnx` +
   `& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe`.

## Verdict format (end of report)
`VERDICT: APPROVE` or `VERDICT: REJECT` + numbered findings with file:line and severity
(BLOCKER / MAJOR / MINOR). REJECT if any BLOCKER. You judge only — fix nothing.
