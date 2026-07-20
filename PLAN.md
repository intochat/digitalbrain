# PLAN — execution scaffolding

**This file is scaffolding, not documentation.** It follows the `NEXT.md` precedent — committed at
`f1bf8e32`, consumed, deleted at `e93ce33c`. Delete it when its plan is consumed. `CLAUDE.md` §6 bans
disposable task checklists *kept as documentation*; a scaffold that names itself and is deleted on
consumption is the form this repository already used once.

The plan of record is `ARCHITECTURE-REVIEW.md`. Where this file and that file disagree, **that file
wins** and this file is wrong.

---

## 0. How to use this

Work top to bottom. Never skip a numbered step. Each step is one of:

| Marker | Meaning |
|---|---|
| `READ` | Gather evidence. No mutation. |
| `PROOF` | Write a test that **must fail**. Run it. Capture the failure text. |
| `RED` | Confirm the proof failed for the *stated reason*, not an unrelated one. |
| `IMPL` | Write only enough code to satisfy the proof. |
| `GREEN` | Run the owning project. Quote the result. |
| `ADV` | Dispatch Refuter, Deleter, Guardian in parallel on the diff. Verify every finding yourself. |
| `GATE` | Run a gate. Quote actual output. Never `--filter`. |
| `COMMIT` | Stage intended files only. Diff-grill answers in the message body. |

**A detail gradient is deliberate.** Phases 2b and 3 are decomposed finely because evidence exists
for them today. Later phases are decomposed to slice level, because pre-deciding the outcome of
proofs not yet run is the failure `ARCHITECTURE-REVIEW.md` §1 names as this repository's root cause.
Expand a later phase *when you reach it*, from the evidence you then have.

## 0.1 Ground rules, compressed

1. **Root gate, every phase:** `dotnet test --logger "console;verbosity=minimal"` from the root.
   Never `--filter`. Background it and poll.
2. **Never a red root gate.** Held red is `[Fact(Explicit = true, DisplayName = "…")]` for xUnit and
   `@ignore @red-until-<reason>` for Gherkin. They behave differently — `-explicit only` *runs* the
   xUnit form; `@ignore` scenarios need `-failSkips` and their bodies never execute.
3. **Every held-red proof is listed in `website/status.md`.** A proof nobody runs is worth nothing
   unless its existence and state are public. The website test enforces this.
4. **Adding a public type** means updating `PublicAPI.Unshipped.txt`. **Adding an `[Alias]`** means
   updating the pinned-alias contract. Both fail the build if forgotten. That is correct.
5. **No comments** as narrative, boilerplate, or commented-out code. Names, types and tests carry
   meaning. Markdown prose is documentation, not a comment.
6. **Ground check** — `git rev-parse HEAD` and `git status --porcelain` at slice start and before
   staging. If something moved that you did not move, **surface it and stop**. Do not revert it, do
   not absorb it.
7. **Evidence precedes assertion.** "Tests pass" is not a claim you may make without the output in
   front of you.

## 0.2 Baseline at the time of writing

| Fact | Value |
|---|---|
| HEAD | `c858254623c8e5bbff7c61cf84eb71a4bacd3513` |
| Root gate | 144 passed, 2 held-red skipped, exit 0 |
| Breakdown | Tests 101 passed · Simulations 38 passed 2 skipped · HostTests 5 passed |
| Explicit held red | `R-5 gate for Phase 3.5: the kernel assembly reaches no vendor model SDK` |
| Website gate | 16/16, 9 features rendered (run from `website/`, `node` directly, never npm) |
| `eng/pack.ps1` | 8 packages + 8 symbol packages, boundary unbreached |
| `eng/verify-consumer.ps1` | both samples ran from an empty package cache |
| Ollama | container-resident, `llama3.1:8b` (`46e0c10c039e`), `mxbai-embed-large` (`468836162de7`) |
| Aspire CLI | 13.4.6 |

## 0.3 Environment realities — do not rediscover these

- **Context7 and codegraph have both been unavailable here.** Quota exhausted; `npx` exiting 9009.
  Say so and fall back to the oracles. Do not skip silently.
- **The compiler is the API oracle.** Write a throwaway file referencing the API and build it. No
  `CS0246` proves the type exists.
- **Microsoft Learn returns `Orleans.EventSourcing.JournaledGrain`** for journaling queries. That is
  a different API from `Microsoft.Orleans.Journaling`. Do not conflate them.
- **Website gates run `node` directly from `website/`.** npm's cmd children lose the nodejs PATH.
- **The samples build from `artifacts/packages`, not project references.** A plain `dotnet build` of
  a sample resolves a stale `0.1.0-alpha.1` from the global cache and fails misleadingly. The real
  gate is `eng/pack.ps1` then `eng/verify-consumer.ps1`, which uses an empty cache.
- **`dotnet build-server shutdown`** before touching packaged files; MSBuild holds them.
- **The source generator string-matches interface names.** `DispatchManifestGenerator` holds
  `DigitalBrain.Abstractions.IHandle<TSynapse>` as a constant. Rename or move those interfaces and it
  silently emits an empty manifest while reflection still finds the handlers.
  `DispatchManifestContracts` catches that divergence. Do not delete it.

---

# Phase 2b — the boundary and the rail

Ends with: a typed request appearing on the feed with no cooperation from the caller, no `Task.Delay`
in any wait path, and an owner boundary that holds against a client.

---

## 2.5 — R-3, the owner boundary

**Consumer today:** `BrainClient` and every simulation that drives one. **Not** an authentication
boundary — DEC-11 defers that, and `status.md` discloses that an Orleans client is a trusted cluster
peer. What 2.5 fixes is that the boundary does not hold *even against an honest caller*.

**The defect, precisely.** Three facts compose into it:

1. `OwnerBoundCallFilter.OwnerOf(context.SourceId)` returns `null` when the source grain key has no
   `/`, which includes every call originating from an Orleans client. A `null` caller skips the
   entire guard block and falls through to `context.Invoke()`.
2. `BrainClient(IGrainFactory, OwnerId)` takes owner as a **constructor argument**.
3. `NeuronHandle.ReadJournalAsync` calls `_grains.GetGrain<INeuron>(...)` **directly**, while
   `BrainClient.FireAsync` routes through `ISessionNeuron`, whose `FireAsync` carries the only owner
   check. `Neuron.ReadJournalAsync` performs no authorization of its own.

So firing cross-owner is refused and reading cross-owner is not. `Client.feature` passes today only
because it exercises the checked verb.

**Target shape.** The session neuron becomes the single client chokepoint. Client reads route through
it exactly as writes do; the filter stops falling through for unattributed callers reaching a
`Neuron` or `SubscriptionRegistry` directly. The client still names its own owner — that is DEC-11's
accepted cost — but there is exactly one door, which is what makes an authenticated door cheap later.

### Slice 2.5.a — prove the read bypass

1. `READ` — `src/DigitalBrain.Kernel/OwnerBoundCallFilter.cs` in full. Confirm the `is { } caller`
   pattern and the unconditional `return context.Invoke();`.
2. `READ` — `src/DigitalBrain.Client/BrainClient.cs`. Note `NeuronHandle.ReadJournalAsync` reaching
   `INeuron` directly and `FireAsync` reaching `ISessionNeuron`.
3. `READ` — `src/DigitalBrain.Kernel/SessionNeuron.cs`. Note the owner check lives here, not in the
   filter.
4. `READ` — `src/DigitalBrain.Kernel/Neuron.cs` `ReadJournalAsync`. Confirm no authorization.
5. `READ` — `src/DigitalBrain.Testing/Simulation.cs`, methods `Client`, `ClientFireAsync`,
   `ClientFireExpectingRefusalAsync`, `ClientReadJournalAsync`. Note `SimulationCluster.Grains` is
   `Deployed().Client` — a genuine external Orleans client, so `OwnerOf` returns `null` for it.
6. `READ` — `src/DigitalBrain.Testing/NeuronSteps.cs` for the existing client step bindings and their
   exact phrasing, so a new step matches house style.
7. `READ` — `tests/DigitalBrain.Simulations/Client.feature`. This is where the proof belongs.
8. `PROOF` — add to `Client.feature`:
   `Scenario: a client cannot read another owner's journal` — given a brain for owner `"tenant-r"`,
   when the client is refused reading the incoming journal of the Echo neuron named `"private"` owned
   by `"tenant-s"`, then the synapse is refused as unauthorized.
9. `IMPL` — add `Simulation.ClientReadJournalExpectingRefusalAsync(kind, neuronType, name,
   targetOwner)` using the existing `CaptureRefusalAsync` helper, constructing the foreign
   `NeuronId` directly so the read targets another owner.
10. `IMPL` — add the matching `[When]` binding in `NeuronSteps.cs`.
11. `GREEN` — build `tests/DigitalBrain.Simulations`. Compile errors only, no behaviour yet.
12. `RED` — run the owning project and confirm the new scenario **fails**, and that it fails because
    the read *succeeded* (the `CaptureRefusalAsync` "expected a refusal, but the synapse was
    accepted" path), not because of a binding or compile problem. Quote the failure.
13. `READ` — record the exact failure text into the eventual commit message. This is the evidence
    that the defect was real before it was fixed.

### Slice 2.5.b — close the read path through the chokepoint

14. `IMPL` — add a journal read verb to `ISessionNeuron` that takes the target `NeuronId`, kind and
    cursor, refuses a receiver whose owner differs from the session's owner with
    `NeuronAuthorizationException`, and otherwise delegates to the target neuron.
15. `IMPL` — mirror `SessionNeuron.FireAsync`'s refusal message shape exactly, so both verbs read the
    same way in a log.
16. `IMPL` — repoint `NeuronHandle.ReadJournalAsync` at the session neuron rather than `INeuron`.
17. `READ` — decide whether `NeuronHandle` still needs the raw `IGrainFactory`. If the session route
    is the only route, the handle carries the session, not the factory. Prefer removing the field to
    leaving it unused.
18. `IMPL` — update `src/DigitalBrain.Abstractions/PublicAPI.Unshipped.txt` for every public surface
    change. The build fails otherwise, which is correct.
19. `IMPL` — if `ISessionNeuron` gains a method, check whether the pinned-alias contract must change.
    Run the alias test to find out rather than guessing.
20. `GREEN` — run `tests/DigitalBrain.Simulations`. The new scenario passes; `Client.feature`'s three
    existing scenarios still pass.
21. `GREEN` — run `tests/DigitalBrain.Tests`. Watch specifically for `PackableSurfaceContracts` and
    `GrainTypeNamingContracts`.

### Slice 2.5.c — stop the filter falling through

22. `PROOF` — add an xUnit contract asserting that an unattributed caller reaching a `Neuron`
    **directly** is refused. This is the §2.6 defect stated as a test rather than a paragraph.
23. `RED` — confirm it fails today.
24. `IMPL` — restructure `OwnerBoundCallFilter` so a `null` caller is a **decision**, not a fall
    through. Unattributed calls to `Neuron` and `SubscriptionRegistry` are refused; unattributed
    calls to `ISessionNeuron` are permitted, because that is the client's one legitimate door.
25. `READ` — verify no in-cluster path depends on the fall-through. Search for direct
    `GetGrain<INeuron>` and `GetGrain<ISubscriptionRegistry>` uses outside the kernel. `ProbeHost`
    and `DigitalBrain.Testing` are the likely callers.
26. `IMPL` — repoint anything that breaks through the session neuron, or give it an in-cluster
    identity. Do not weaken the filter to accommodate a caller.
27. `GREEN` — run `tests/DigitalBrain.Tests`, then `tests/DigitalBrain.Simulations`.
28. `GATE` — root gate. Expect the prior 144 plus the new proofs, still 2 held-red skipped.
29. `GATE` — `tests/DigitalBrain.HostTests`. The hosted proof drives from `ProbeHost` *inside* the
    cluster; if 2.5.c changed what an in-cluster caller may do, this is where it shows.

### Slice 2.5.d — the endpoint invariant

R-3: *"the plan must state that the Orleans client endpoint is never publicly reachable as a
load-bearing invariant with a test, not a deployment footnote."* R-3 also admits this is *"insufficient
on its own"*, which under DEC-11 is exactly why it is written down rather than relied upon.

30. `READ` — `hosts/DigitalBrain.AppHost/AppHost.cs` and `src/DigitalBrain.Aspire.Hosting` for how
    the silo's endpoints are declared today.
31. `READ` — `tests/DigitalBrain.HostTests/PublishManifest.cs`. A manifest assertion may already be
    the right home for this proof.
32. `PROOF` — assert the silo's Orleans gateway endpoint is not published on a public interface in
    the AppHost topology. Inspect the resource model; do not bind a socket.
33. `RED` — run it. If it passes immediately the invariant already holds; say so and keep the proof
    as a regression guard rather than pretending it was a fix.
34. `IMPL` — only if red: constrain the endpoint in the AppHost.
35. `GATE` — root gate, background, poll, quote.

### Slice 2.5.e — adversaries and close

36. `ADV` — Refuter: *"Find the caller that still reaches another owner's state. Try the registry,
    try the session neuron of an owner you do not own, try a neuron type that is not `Neuron`.
    Default to refuted when uncertain; a passing test is not evidence the behaviour is right."*
37. `ADV` — Deleter: *"What in this diff has no consumer today? Did `NeuronHandle` keep a field it no
    longer uses? Did the filter gain a branch nothing exercises? Propose the deletion, not a
    refactor."*
38. `ADV` — Guardian: *"Quote DEC-11 and `status.md`'s trusted-peer disclosure beside this diff. Does
    2.5 claim to be an authentication boundary anywhere in code, test name, or prose? It must not."*
39. `READ` — verify every finding yourself. Resolve it or reject it in writing with evidence.
40. `IMPL` — update `website/status.md`: the trusted-peer debt stays, but the read/fire asymmetry is
    now closed and must no longer read as open.
41. `GATE` — website gate from `website/`: `node tools/render-specification.mjs` then
    `node --test tests/*.test.mjs`.
42. `GATE` — root gate, quoted.
43. `READ` — `git rev-parse HEAD` and `git status --porcelain`. Compare against slice start.
44. `COMMIT` — stage intended files only. Body carries: what was added with no consumer today; what
    was claimed without a command; what changed that you did not change; and the adversary
    dispositions.

---

## 2.6 — `WatchAsync` and `FeedNeuron`

**Consumer:** the multiagent sample's polling loop (D-6, step 2.10) and every out-of-process observer.
**Deletes:** both polling loops. §12 rejects *"server-side polling behind a streaming API"* by name.

45. `READ` — `src/DigitalBrain.Testing/SynapseObserver.cs`. It is an `ActivityListener` over
    `SynapseTelemetry` — push-based, no polling, already correct. What is missing is durability and
    catch-up, not observation.
46. `READ` — `ARCHITECTURE-REVIEW.md` §4.1 in full. One durable feed per **identity**, not per
    connection, is the load-bearing invariant.
47. `READ` — `src/DigitalBrain.Kernel/NeuronFeed.cs` for the existing snapshot/delta/cursor shape.
48. `READ` — DEC-1's scope correction: for incoming/outgoing journals the snapshot is a durable
    *summary*, not latest-per-key. The literal latest-per-key snapshot is a requirement on **this**
    step's keyed per-identity feed.
49. `PROOF` — a scenario where an observer disconnects, facts continue to arrive, the observer
    reconnects with its cursor, and receives everything it missed in order, with no `Task.Delay` in
    the wait path.
50. `PROOF` — a scenario where an observer's cursor has fallen off the retention window and it
    receives a **reset carrying a full snapshot and a resume sequence** — never a gap, never silence.
51. `PROOF` — a contract asserting no `Task.Delay` appears in any wait path reachable from
    `WatchAsync`. Prefer a structural assertion over a grep.
52. `RED` — run all three. Quote failures.
53. `IMPL` — `WatchAsync` on `INeuron`. Push-based. No server-side timer.
54. `IMPL` — `FeedNeuron` as an ordinary neuron, not a special kind. §4.1's collapse.
55. `IMPL` — `PublicAPI.Unshipped.txt` for the new surface.
56. `GREEN` — owning project.
57. `IMPL` — delete the polling loop this replaces in `DigitalBrain.Testing`, if one exists there.
58. `ADV` — Refuter: *"Prove a fact can be missed. Race a disconnect against an emit. Race a cursor
    reset against a write."* Deleter: *"Is `FeedNeuron` a second neuron kind? If it needs any special
    casing in the kernel, it is, and DEC-8's whole argument breaks."* Guardian: *"Quote §12's polling
    row against the wait path."*
59. `READ` — verify findings.
60. `GATE` — root gate. `IMPL` — status.md: the "No timeline stream" debt is now closed; rewrite it.
61. `GATE` — website gate.
62. `COMMIT`.

---

## 2.7 — call-filter reification

**This is the bridge that makes DEC-6, DEC-9 and DEC-8 all work.** A typed request appears on the
caller's feed with no cooperation from the caller. There is a held-red Gherkin scenario waiting for
it: `Fabric.feature`, tagged `@ignore @red-until-phase-2.7`.

63. `READ` — `tests/DigitalBrain.Simulations/Fabric.feature`, the `@red-until-phase-2.7` scenario.
    This is the acceptance criterion; do not invent a different one.
64. `READ` — §14.3's resolution: *"Domain facts are feed-worthy. Call traffic is traced and reified,
    not accumulated."* Reification must not evict domain events from a bounded feed.
65. `READ` — §14.1's resolution: reification records on the **caller's** feed, which is why keying a
    model neuron by correlation costs nothing.
66. `PROOF` — remove `@ignore` from that scenario locally and run it to observe the real failure.
    Restore the tag if the slice is not completed in one sitting.
67. `RED` — quote the failure.
68. `IMPL` — an outgoing grain call filter that reifies the call onto the caller's feed: target
    interface, method, correlation, causation, sequence, timestamp.
69. `IMPL` — ensure a behaviour that makes ten model calls does not evict its own domain events.
    §14.3 is explicit that the bound protects storage and CPU, not signal-to-noise.
70. `GREEN` — owning project with the tag removed.
71. `IMPL` — remove `@ignore @red-until-phase-2.7` permanently.
72. `IMPL` — `website/status.md`: strike that row from *proofs held red*.
73. `ADV` — Refuter: *"Find the call that is not reified. Try a self-grain call, a call from a timer,
    a call inside a handler."* Deleter: *"Does the envelope carry a field nothing reads?"* Guardian:
    *"DEC-9 says both verbs are journaled and neither is privileged. Is that true of this diff?"*
74. `READ` — verify findings.
75. `GATE` — root gate; expect one fewer skip.
76. `GATE` — website gate.
77. `COMMIT`.

---

## 2.8 — R-4 and broadcast addressing, as one change

**One change, not two.** §9 is explicit. `Fabric.feature` carries
`@ignore @red-until-broadcast-addressing-is-decided`, and `status.md` already records that the
decision is *made* and the proof now has a target.

78. `READ` — `status.md`'s "Proofs held red" section, the long note under the table. It states the
    design: a broadcast addresses handler **types**, not instance names; the type set is known at
    composition time because interfaces declare it; the instance is derived from the firing
    correlation. Two prior generations converged on this independently.
79. `READ` — `src/DigitalBrain.Kernel/SubscriptionRegistry.cs` and `Neuron.OnActivateAsync`'s
    registration.
80. `READ` — R-4's three defects: never-activated neurons miss broadcasts; subscriptions are never
    removed so fan-out grows monotonically; the per-owner registry is a single-threaded hot spot on
    every emit and every activation.
81. `PROOF` — remove the `@ignore` locally; run; quote the failure.
82. `PROOF` — a scenario asserting fan-out does not grow across repeated activation cycles.
83. `IMPL` — populate the registry at composition time from the declared interfaces, not at
    activation.
84. `IMPL` — derive the broadcast instance from the firing correlation per DEC-6's keying.
85. `IMPL` — remove the registration write from the activation path. This is the hot-spot fix and it
    is the reason the change is worth making beyond correctness.
86. `READ` — behaviours are the deliberate exception: a script's handled set cannot be known before
    the script exists, so behaviours register at install. Keep that path bounded and explicit; it is
    not the per-activation write.
87. `GREEN` — owning project.
88. `IMPL` — remove the `@ignore` tag permanently; strike the row from `status.md`.
89. `IMPL` — `status.md`: close the "Subscriptions are never removed" and "A neuron that has never
    activated does not receive broadcasts" debts.
90. `ADV` — Refuter: *"Find the broadcast that reaches the wrong instance, or reaches none."*
    Deleter: *"Is the registry grain still needed at all?"* Guardian: *"§9 says 2.8 is ONE change.
    Did this become two?"*
91. `READ` — verify findings.
92. `GATE` — root gate; expect zero Gherkin skips remaining.
93. `GATE` — website gate.
94. `COMMIT`.

---

## 2.9 — R-2, per-receiver outbox progress

**State the ordering guarantee first.** §11: *"no prior generation documents one."* This is a
decision to make and record, not code to write first.

95. `READ` — §11's delivery-ordering entry in full. Two fragments are offered: ino's *"best-effort
    ordering within a target, at-least-once, idempotency is the handler's responsibility"*, and its
    split by verb where a broadcast returns reached/failed counts and *"one listener's failure doesn't
    fail the broadcast."*
96. `READ` — DEC-9. **A directed request and an undirected fact cannot share an ordering guarantee.**
    DEC-9 already separates them, so the guarantee must be stated per verb.
97. `READ` — `src/DigitalBrain.Kernel/OutboxEntry.cs` and the drain loop. Confirm it drains strictly
    in order and stops at the first undelivered receiver.
98. **DECISION — bring to the human with a recommendation before implementing.** Recommended:
    per-target FIFO for directed requests; at-least-once with handler-owned idempotency; no
    cross-target ordering; a broadcast never fails as a whole because one receiver failed. Record it
    in `ARCHITECTURE-REVIEW.md` §3 or §11 before writing code.
99. `READ` — `tests/DigitalBrain.Simulations/Fabric.feature`, the
    `@red-until-...` scenario for the blocked receiver, and `BlockedDeliveryNeurons.cs`.
100. `PROOF` — run the held-red scenario for 1.3 and quote its failure.
101. `IMPL` — per-receiver progress so one unreachable receiver cannot stall traffic to reachable
     ones.
102. `GREEN` — owning project.
103. `IMPL` — `status.md`: close "One unreachable receiver blocks a neuron's whole outbox", and state
     the newly documented ordering guarantee where a reader will find it.
104. `PROOF` — "Outbox redelivery is unproven" is an open debt in `status.md`: code without a proof.
     While in this file, drive a receiver outage and assert the redelivery. Close the debt or leave it
     open honestly.
105. `ADV` — Refuter: *"Violate the guarantee you just documented. Two senders, one receiver, a
     restart mid-drain."* Deleter: *"Did per-receiver progress add state that nothing reads?"*
     Guardian: *"Is the documented guarantee the one the code provides, or the one you wished for?"*
106. `READ` — verify findings.
107. `GATE` — root gate. `GATE` — website gate.
108. `COMMIT`.

---

## 2.10 — D-6, delete the sample's workaround

109. `READ` — `samples/DigitalBrain.Multiagent/Program.cs`. Find the activation workaround — a dummy
     `ReadJournalAsync` per panellist whose only purpose is to force registration — and the
     `for (var probe = 0; probe < 100; probe++)` polling loop with `Task.Delay(100)`.
110. `IMPL` — delete the activation workaround. 2.8 made it unnecessary.
111. `IMPL` — replace the polling loop with `WatchAsync` from 2.6. The flagship sample is the
     showcase; it must demonstrate the primitive, not work around its absence.
112. `GATE` — `dotnet build-server shutdown`, then `eng/pack.ps1`, then `eng/verify-consumer.ps1`.
     The samples build from the local feed against an **empty** cache; a plain `dotnet build` will
     mislead you.
113. `READ` — quote `"both samples restored, built and ran from the local feed against an empty
     package cache"` or the failure.
114. `IMPL` — `status.md`: the multiagent workaround is referenced in the broadcast debt; remove that
     reference.
115. `ADV` — Deleter especially: *"Is the sample now smaller? If D-6 did not reduce it, it did not
     land."*
116. `GATE` — root gate. `GATE` — website gate.
117. `COMMIT`.

118. `READ` — **Phase 2b boundary.** Confirm against §9's stated end state: a typed request appears on
     the feed with no caller cooperation; no `Task.Delay` in any wait path; the owner boundary holds
     against a client. If any of the three is not demonstrable, Phase 2b is not done — say which and
     continue.
119. `GATE` — full gate: `dotnet test .\DigitalBrain.slnx -c Release`, `eng/pack.ps1`,
     `eng/verify-consumer.ps1`, `eng/verify-dependencies.ps1`.
120. `ADV` — a real adversarial review at the phase boundary, per `CLAUDE.md` §3. Verify its findings
     yourself; a review is a claim like any other.

---

# Phase 3 — Modules. B-2, then B-4, which proves B-2.

## 3.1 — `IModule`, descriptor, `AddModule<T>()`

121. `READ` — §4.2 and DEC-2 as scope-corrected. `AddModule<T>()` is a **builder call, not an
     installer**. There is no archive, no signature verification, no collectible `AssemblyLoadContext`,
     no quarantine world. §12 rejects all of it.
122. `READ` — §14.6's resolution: **the descriptor never declares neurons or synapse types.** Three
     prior generations independently did not. Wiring stays on the interfaces and is generated from
     them at 3.3. If you put neurons in the descriptor you have recreated the tension §14.6 closed.
123. `PROOF` — a contract asserting a module descriptor carries configuration, secrets, capabilities,
     effects and connection declarations, and **no** neuron or synapse types.
124. `PROOF` — a contract asserting composition-time validation rejects a module whose declarations
     are inconsistent, at `builder.Build()`, not at first use.
125. `RED` — run both.
126. `IMPL` — `IModule`, the descriptor, `AddModule<T>()`, composition-time validation.
127. `IMPL` — `PublicAPI.Unshipped.txt`.
128. `GREEN` — owning project. `ADV`. `GATE` root + website. `COMMIT`.

## 3.2 — the two-package template

129. `READ` — DEC-3 **as amended**: two packages, not three. `.Testing` is deferred to 3.7 and
     returns only with a second implementation to conform against.
130. `READ` — DEC-3's second job for `.Contracts`: under DEC-8 a script compiles against a reference
     set and **that reference set is its capability surface**. A script that cannot name `IGmail`
     cannot reach Gmail. The split is load-bearing on day one.
131. `PROOF` — the contracts guard: `.Contracts` is a leaf with zero dependencies; the implementation
     package may reference vendor SDKs and the contracts package may not.
132. `RED` — run it against a deliberately-wrong template instance.
133. `IMPL` — the template, with the guard test baked in so every module inherits it.
134. `GREEN`. `ADV` — Deleter: *"Is the template a template, or a copy waiting to rot?"* `GATE`.
     `COMMIT`.

## 3.3 — R-6, generate the contract manifest

135. `READ` — §11's narrowed R-6 entry: the generator is **load-bearing regardless** of the dispatch
     outcome, because §4.2 makes the contract manifest the thing a descriptor's wiring half is
     generated from.
136. `READ` — `src/DigitalBrain.SourceGeneration/DispatchManifestGenerator`. It string-matches
     `DigitalBrain.Abstractions.IHandle<TSynapse>`. `DispatchManifestContracts` catches divergence.
137. `PROOF` — the generated manifest matches what reflection finds, for a module.
138. `IMPL` — generate the wiring half of the descriptor from the interfaces.
139. `IMPL` — decide R-6's open half with measurement: promote the generated manifest or delete the
     reflection path. Record the measurement, not an opinion.
140. `GREEN`. `ADV`. `GATE`. `COMMIT`.

## 3.4 — B-3, connections

141. `READ` — DEC-5 in full. The kernel owns the state machine
     `NotConfigured → Authorizing → Authorized → Expired → Revoked`, token storage and refresh, the
     consent flow, and a **closed health union**. The module supplies only provider-specific token
     exchange and refresh.
142. `READ` — DEC-5's three distinct declarations — app credential, user authorization, capability
     grant. They must not be collapsed; they have different scopes and lifetimes.
143. `READ` — DEC-5: *"generic bool+string health is a conformance failure."* Health is
     `Healthy | MissingAppCredentials | NotConfigured | NotAuthorized | TokenExpired | ProviderError
     | NetworkError`. A boolean cannot drive a UI that must distinguish "an administrator must
     configure this" from "you must sign in" from "retry".
144. `READ` — DEC-5: **module isolation is not attempted, deliberately.** Capability gating of
     in-process code is theatre; the prior art documents its own escape hatches. Do not build it.
145. `READ` — §14.2: encryption at rest narrows the blast radius and is worth having **in addition
     to** the boundary, not instead of it. Under DEC-11 there is no authentication boundary here, so
     say plainly in `status.md` what a connection neuron's tokens are protected by.
146. `PROOF` — the health union is closed and exhaustively handled.
147. `PROOF` — a module cannot implement auth itself; the kernel owns the state machine.
148. `IMPL` — connections, health union, provider-adapter contract.
149. `GREEN`. `ADV` — Guardian: *"Quote DEC-5's isolation paragraph. Did this diff build gating?"*
     `GATE`. `COMMIT`.

## 3.5 — B-4, the AI module. Turns the R-5 held-red proof green.

150. `READ` — the held-red proof `R-5 gate for Phase 3.5: the kernel assembly reaches no vendor model
     SDK` in `tests/DigitalBrain.Tests/AssemblyBoundaryContracts.cs`. **This step exists to turn it
     green.**
151. `READ` — DEC-6 as resolved, including §14.1. A model neuron is **keyed by the ambient correlation
     id**. Each call gets its own activation, nothing serialises across callers, `[Reentrant]` is
     never needed, and `History[^1].Sequence == LastSequence` is never at risk.
152. `READ` — DEC-6's recorded risk: per-call overhead is unmeasured. Ten model calls is ten
     activations and ten journal writes. **Measure it here.** If too costly the fallback is a raw
     client plus explicit tracing — *not* the synapse hop, which `digitalbrain` built and invoked
     zero times.
153. `READ` — DEC-6's third correction: the typed types must be **on the call path**, not a catalog
     decoration beside it. A call that is not on the feed is a call that did not go through the rail,
     which is a testable property. Test it.
154. `PROOF` — run the R-5 proof with `-explicit only` and quote its current failure.
155. `PROOF` — a typed model call appears on the caller's feed via 2.7's reification, with provider
     and model identity.
156. `PROOF` — concurrent calls to one model do not serialise.
157. `IMPL` — the AI module: model neurons keyed by correlation, provider adapters behind
     `.Contracts`.
158. `IMPL` — **D-1 lands here as a consequence.** `ModelTier` is 75 references across 23 files; it
     goes when the AI concern leaves the kernel. `ModelTier.Embedding` cannot work — an embedding
     model is `IEmbeddingGenerator<string, Embedding<float>>`, not an `IChatClient` — and it is
     deleted rather than fixed in place.
159. `IMPL` — remove the vendor SDK references from `DigitalBrain.Kernel`.
160. `IMPL` — update `hosts/DigitalBrain.AppHost/AppHost.cs` and `TestingAppHost`, which both wire
     `WithModel(ModelTier.Balanced, ModelProviders.OpenAi, …)` today.
161. `IMPL` — **add the Ollama provider adapter and AppHost resource here.** Phase 7's live
     acceptance needs a real local model; this is the step that provides it. Reach Ollama by
     materialized endpoint URL, per the harvest — `OllamaApiClient` builds its own `HttpClient` and
     bypasses the DI handler chain, so service discovery does not apply. MCP must never call Ollama;
     only the model neuron does.
162. `IMPL` — remove `[Fact(Explicit = true)]` from the R-5 proof; it is green now.
163. `IMPL` — `status.md`: strike the R-5 row from proofs held red; close the `Embedding` tier debt.
164. `GREEN`. `ADV` — Refuter: *"Find the inference that bypasses the typed neuron."* `GATE` root +
     pack + consumer + website. `COMMIT`.

## 3.6 — R-5, re-justify or delete the metapackage

165. `READ` — R-5. The kernel is clean now; the reason for the metapackage may have gone with it.
166. `IMPL` — re-justify in writing or delete. `PackableProjects.Names` and the website's package
     pages both change if it goes.
167. `GATE`. `COMMIT`.

## 3.7 — a second provider adapter

168. `READ` — §9: this is *"the real proof of 3.1–3.3, and the first thing `.Testing` could
     conform. DEC-4 returns here or not at all."*
169. **DECISION — Tier-1's stated purpose. §11 requires this decided before 3.7.** Bring to the human
     with a recommendation. §11 offers two discriminators better than "Gherkin": a **numeric speed
     budget per tier** — *"a tier that cannot state its budget is not a tier"* — and **what is real**
     — mocked model → real model → real everything. Recommended: keep Tier-1 with a stated budget and
     a fidelity criterion, because DEC-8 makes *"an AI-authored neuron has no human to hand-write its
     xUnit"* literal, so the gate must be machine-runnable.
170. `IMPL` — the second adapter against the same template.
171. `IMPL` — DEC-4's `.Testing` package, or record that it is dropped.
172. `GREEN`. `ADV`. `GATE`. `COMMIT`.
173. `ADV` — Phase 3 boundary review.

---

# Phase 4 — The scripting rail. DEC-8, and B-6 as its gate.

**This is the product.** Everything before it is prerequisite.

## 4.1 — the client API surface

174. `READ` — DEC-8 in full, and the README example. The surface is `Connect`, `Get<T>`,
     `Get<T>(name)`, `On<T>`, `Emit`. **No globals, no attributes, no base class, no framework
     vocabulary.**
175. `READ` — DEC-8's addressing rule: `Get<T>()` resolves `(T, ambient owner, "default")`. **Owner
     is always ambient and never a parameter**, so a script *cannot state* another owner's neuron.
     The boundary becomes unstateable rather than merely enforced.
176. `READ` — DEC-12: shape `Connect()` to carry ambient identity now, so 4.5 adds a field rather
     than breaking a shipped surface. Do not build the actor itself here.
177. `PROOF` — the same file compiles and runs as an external script and installs as a behavior. This
     is the DEC-8 claim; prove it early because everything rests on it.
178. `PROOF` — a script cannot name another owner. Assert the API has no overload that accepts one.
179. `IMPL` — the client API.
180. `IMPL` — `PublicAPI.Unshipped.txt`.
181. `GREEN`. `ADV` — Guardian: *"Does any verb take an owner?"* `GATE`. `COMMIT`.

## 4.2 — Roslyn compilation against a `.Contracts` reference set

182. `READ` — DEC-3: the reference set **is** the capability surface.
183. `READ` — §11's DSL entry: DEC-8 is deliberately **not** a fourth interpreter attempt. It is C#
     compiled by Roslyn, so the type checker is the gate and there is no interpreter to demote.
184. `PROOF` — a script compiles against exactly the permitted contracts.
185. `PROOF` — invalid C# fails loudly with useful diagnostics and creates no installable proposal.
186. `PROOF` — a forbidden framework, vendor or project reference fails.
187. `PROOF` — an extra source file fails; one script means one file.
188. `PROOF` — undeclared capability use fails.
189. `RED` — all five. Each must fail for its own stated reason.
190. `IMPL` — the restricted compilation pipeline. `Microsoft.CodeAnalysis.CSharp` is already in
     `Directory.Packages.props` at 4.14.0.
191. `IMPL` — bind compilation to a **source digest, reference-set digest and compiler identity**.
     4.5 needs these to make approval tamper-evident.
192. `GREEN`. `ADV` — Refuter: *"Get a reference past the gate. Try an alias, a global using, an
     extern alias, a transitive reference."* `GATE`. `COMMIT`.

## 4.3 — `IBehavior : INeuron`

193. `READ` — DEC-8: **one grain type, registered at startup.** The script is durable state, not a
     type. A new behavior is a new *instance*, and Orleans grains are virtual so every instance name
     already exists. This is why DEC-2's constraint is untouched.
194. `READ` — the verified fact from `NEXT.md`: `NeuronId.ToGrainId()` is `GrainId.Create(Type,
     GrainKey)` where `Type` is a string Orleans resolves against a manifest fixed at silo startup.
     **This is why behaviours add no grain type.**
195. `READ` — `INeuron` has three methods, none behaviour-specific. Every neuron is already
     wire-identical, which is why `IBehavior` needs no new wire contract.
196. `PROOF` — installing a behavior adds no grain type to the Orleans manifest. Compare the manifest
     before and after.
197. `PROOF` — a behavior survives a silo restart with its script intact.
198. `IMPL` — `IBehavior`, script as durable state, top level as activation path.
199. `GREEN`. `ADV` — Guardian: *"Is this a second neuron kind? If the kernel special-cases it
     anywhere, DEC-8's central argument has broken."* `GATE`. `COMMIT`.

## 4.4 — the recording pass

200. `READ` — DEC-8: the script runs once with a context where `On<T>` and `Get<T>` **record instead
     of act**. The output is the manifest — handled facts, requested capabilities, emitted facts —
     and *that* is the approval screen. It is derived from the code, so it cannot drift from it.
201. `PROOF` — during recording, **zero** external calls, subscriptions, durable side effects or
     emitted live facts occur.
202. `PROOF` — the manifest lists exactly the facts handled, capabilities requested and facts emitted.
203. `PROOF` — a script cannot hold a capability it did not ask for, because asking is how it gets
     recorded.
204. `PROOF` — the manifest is bound to the source, reference-set and compiler digests from 4.2.
205. `RED` — all four.
206. `IMPL` — the recording context.
207. `GREEN`. `ADV` — Refuter: *"Make the recording pass cause a side effect. Try a static
     constructor, a finalizer, a `Task.Run`, a module initializer."* `GATE`. `COMMIT`.

## 4.5 — B-6, the governance ledger. The gate on the product.

208. `READ` — DEC-1a: governance gets its **own append-only ledger**, separate from the bounded feed.
     Proposals, approvals, installs, rollbacks, grant/revoke. Bounded feed for traffic, unbounded
     ledger for governance. No prior generation drew this line cleanly.
209. `READ` — DEC-12: **the actor lands here.** This is its first consumer.
210. `READ` — DEC-8: *"Every install is a human-approved proposal, including a behavior authoring or
     modifying a behavior."* The script is the state, so approval is a journal entry and rollback is
     reverting one.
211. `IMPL` — the actor/principal, ambient, carried on the rail alongside owner.
212. `PROOF` — proposer and approver are recorded distinctly.
213. `PROOF` — **self-approval is refused.** The same actor cannot approve its own proposal. This is
     DEC-8's safety gate and the reason the rail is acceptable at all.
214. `PROOF` — installing before approval fails.
215. `PROOF` — approval is bound to the exact source digest, reference-set digest, compiler identity
     and derived manifest. Changing any of them after approval invalidates installation.
216. `PROOF` — the ledger is append-only; a rollback is a new entry, never a deletion.
217. `PROOF` — reverting the governance entry rolls the behavior back **in the same running silo**:
     the previous version is restored, future inputs no longer execute the reverted version, and
     proposal → approval → install → rollback stays inspectable.
218. `PROOF` — after a restart, approvals, current behavior state, rollback history and ledger state
     reconstruct correctly.
219. `RED` — all seven.
220. `IMPL` — the ledger, propose → approve → install, journaled and reversible.
221. `IMPL` — `status.md`: DEC-1's note that *"the audit log it used to pretend to be is provided
     separately by the governance ledger, which is not built yet"* is now false. Fix it.
222. `GREEN`. `ADV` — Refuter: *"Approve your own proposal. Tamper with the source after approval.
     Install twice. Roll back twice. Race a proposal against an install."* Guardian: *"Is this a
     safety property described as a security property anywhere?"* `GATE`. `COMMIT`.

## 4.6 — capability enforcement at `Get<T>()`

223. `READ` — DEC-8: capability is enforced at `Get<T>()` **and only for behaviors**. The boundary is
     **script versus compiled code**. DEC-5 already concedes gating in-process compiled code is
     theatre; `final` proved it by shipping `BundleHasGrant` as a hardcoded `return false` whose own
     audit found grants evaluated after the privileged call had fired.
224. `PROOF` — a behavior resolving an ungranted capability is refused at `Get<T>()`.
225. `PROOF` — compiled module code is **not** gated, and this is asserted deliberately so the
     absence is not mistaken for an oversight.
226. `IMPL`. `GREEN`. `ADV`. `GATE`. `COMMIT`.

## 4.7 — external hosting mode

227. `READ` — DEC-8: *"Both hosting modes ship together, gated on R-3."* 2.5 is that gate.
228. `READ` — DEC-11: the external script is a trusted cluster peer. `status.md` must say so plainly
     for this mode.
229. `PROOF` — the identical file used in 4.1's proof runs as an external cluster client.
230. `IMPL`. `GREEN`. `ADV`. `GATE`. `COMMIT`.

## 4.8 — the generation benchmark

**The one assumption that can invalidate the goal.** Nobody has measured whether a model can reliably
emit these scripts.

231. `READ` — §11's corrected DSL entry and §10's harvest. `final` claimed *"20 prompts against an 80%
     bar, scored 60%, formally demoted the language."* Both halves are contradicted by its own
     documents: *"Execution used deterministic stub returning 'first attempt' JSON"* and *"its 60% is
     simulated, not measured"* — and the demotion did not hold, because the interpreter *"landed
     afterward and is green."*
232. `READ` — the two corrections that failure forces. **(1) The benchmark runs against the real
     model or it does not count.** **(2) Demotion is enforced against the codebase, not the spec.**
233. **DECISION — bring to the human before running anything.** The prompt corpus, the numeric
     threshold, and the model configuration. A threshold set after seeing results is worthless.
     Recommended: ~20 realistic intents with the vocabulary they may use; held-out cases never used
     for tuning; `llama3.1:8b` at a pinned digest; score = compiles against only the permitted
     contracts **and** does what the intent asked.
234. `IMPL` — commit the corpus and threshold **in writing, before execution**, in
     `ARCHITECTURE-REVIEW.md`.
235. `IMPL` — the harness. Persist per case: model name, model digest, endpoint identity, prompt,
     parameters, raw generated file, file digest, diagnostics, latency, score.
236. `GATE` — run it against the real configured Ollama model. CPU must work; GPU may be faster but
     cannot be a correctness prerequisite.
237. `READ` — score it. Do not tune the threshold now.
238. `IMPL` — **if it fails the bar, enforce that against the codebase**, not the specification. A
     gate that fires while the machinery ships anyway is theatre with a number attached.
239. `IMPL` — `status.md`: the load-bearing unmeasured assumption is now measured. State the number.
240. `ADV`. `GATE`. `COMMIT`. `ADV` — Phase 4 boundary review.

---

# Phase 5 — UI, reduced

241. `READ` — DEC-7 as amended. A UI component is a **neuron interface in a UI module's
     `.Contracts`**, resolved like any other capability. Deleted: the component catalog as a
     registry, vocabulary negotiation as a handshake, the surface protocol as a wire format.
242. `READ` — DEC-7's two surviving rules. Unknown component is a hard error — free, because a script
     that does not reference the contract does not compile. Component-local state never round-trips —
     hover, focus, toggle, text entry and scroll are client-local; only committed intent becomes a
     synapse.
243. `READ` — a UI interaction is an **unbounded wait for a human**, so it is the canonical case for
     DEC-9's durable request. The UI validates that mechanism rather than straining it.
244. `PROOF` — 5.1: a durable request survives a restart while waiting for a human.
245. `IMPL` — 5.1 durable request/response.
246. `IMPL` — 5.2 UI module `.Contracts` with component interfaces.
247. `IMPL` — 5.3 **as reduced**: the Dart contracts package plus a build-time drift guard that fails
     the build when the Dart package does not implement what the C# contract declares. The C#
     declaration is the source of truth. **No live transport in this phase.**
248. `IMPL` — `status.md`: state plainly that no live UI connection exists and where it would come
     from.
249. `ADV`. `GATE`. `COMMIT`.

---

# Phase 6 — Cleanup

250. `READ` — §9: deliberately last of the framework phases, because these are the cheapest items and
     the most tempting to start with, and starting with them produces motion without progress.
251. `IMPL` — D-2, delete `IAnswer`.
252. `IMPL` — D-4, vestigial identity code.
253. `IMPL` — R-7, the Pre-Change Ritual and `CLAUDE.md`. Note `Directory.Build.targets` runs a
     codegraph init that has been failing here; either fix it or delete it rather than leaving a
     warning nobody acts on.
254. `IMPL` — §7's surviving renames. D-3 and the `Stamped` rename are **not** here; DEC-10 absorbed
     both into 2.4.
255. `IMPL` — close or restate every remaining `status.md` debt. `AsClient()` leaking a connection
     string must be closed before any production posture is claimed.
256. `GATE` — full gate. `COMMIT`. `ADV` — Phase 6 boundary review.

---

# Phase 7 — The MCP edge and live acceptance

**Placed after Phase 6 deliberately:** cleanup mutates the code the acceptance run exercises.

## 7.1 — composition guard

257. `PROOF` — every intended product and test project is in `DigitalBrain.slnx` and reached by the
     root gate, or carries a concrete present justification. Known-good exclusions today:
     `ProbeHost` builds transitively via `TestingAppHost`; both samples are package-only consumers
     verified by `eng/verify-consumer.ps1`.
258. `RED` — introduce a throwaway excluded project and confirm the guard catches it. Delete it after.
259. `IMPL`. `GREEN`. `GATE`. `COMMIT`.

## 7.2 — the thin adapter

260. `READ` — DEC-11. Thin client of the client API. Owns no kernel, journal, runtime, registry or
     source of truth. It adds **no vocabulary the client API does not already expose.**
261. `READ` — the harvest: use the official ASP.NET MCP server package and an official MCP client in
     tests. The prototype had `ModelContextProtocol` / `ModelContextProtocol.AspNetCore` 1.4.0 and
     **never wrote the live client round-trip** — its own plan item *"List DigitalBrain MCP tools"*
     is unchecked. Write it.
262. `IMPL` — add the packages to `Directory.Packages.props` at deliberate latest versions.
263. `IMPL` — the adapter, targeting `net10.0`, referencing only current projects, in the solution.
264. `PROOF` — protocol conformance: initialize handshake, initialized notification, `tools/list` and
     schemas, a real tool invocation.
265. `IMPL` — health and readiness endpoints.
266. `GREEN`. `ADV` — Deleter: *"Is this over 50 lines of logic? What is it doing that the client API
     does not already do?"* `GATE`. `COMMIT`.

## 7.3 — topology guard

267. `READ` — the harvested AppHost shape. Resource named `mcp`. Two Aspire-managed proxied endpoints
     with no declared port. One **non-proxied** endpoint with `port: 5000, targetPort: 5000`. The
     literal `http://localhost:5000` appended to `ASPNETCORE_URLS` alongside two interpolated target
     ports — the prototype's test asserted exactly two dynamic value providers, which proves the
     third URL is a literal. Loopback binding via `localhost`, never `0.0.0.0`.
268. `PROOF` — assert that topology against the **resource model**, without binding a socket, so the
     test cannot collide with a running AppHost.
269. `PROOF` — internal Orleans and service endpoints are private and dynamically allocated.
270. `IMPL` — the AppHost composition: `mcp` resource, `WaitFor` on silo and durable storage, and the
     Ollama resource from 3.5 with persistent storage.
271. `IMPL` — root `aspire.config.json` pointing at `hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`.
272. `IMPL` — the `digitalbrain` entry in root `.mcp.json`:
     `{"type": "http", "url": "http://localhost:5000/mcp"}`.
273. `IMPL` — port-collision handling: report the owning process and fail cleanly. **Never kill an
     unrelated process.** The prototype had none of this and could not run two AppHosts side by side.
274. `GREEN`. `ADV`. `GATE`. `COMMIT`.

## 7.4 — transport limits

275. `IMPL` — the harvested ladder: 413 on oversize body including a negative `ContentLength`, 429 on
     rate or concurrency exhaustion, 504 on timeout. **Long-lived feeds are exempted from the request
     timeout rather than the timeout being disabled globally.**
276. `IMPL` — error logging records `exception.GetType().Name` only; secrets and tokens are redacted
     from logs and artifacts.
277. `PROOF` — each limit, driven for real.
278. `GREEN`. `ADV`. `GATE`. `COMMIT`.

## 7.5 — the live acceptance sequence

**A smoke test, `tools/list`, HTTP 200, "accepted", or Ollama health alone is not acceptance.**

279. `IMPL` — a committed, repeatable acceptance driver. Not a one-off script.
280. `READ` — record clean git SHA and status, and build outputs.
281. `READ` — preflight port 5000 and Ollama without killing unrelated processes.
282. `IMPL` — drive Aspire MCP: discover and validate the AppHost, start the topology, observe
     storage, silo, MCP, Ollama and model resources, inspect readiness, health, logs and traces.
283. `PROOF` — MCP `/health`; real initialize handshake; initialized notification; `tools/list` and
     schemas; a real tool invocation; Ollama model availability; one real inference with provider and
     model telemetry.
284. `PROOF` — through the live model path, Ollama emits **exactly one** ordinary C# file using only
     the public client API. Persist the exact source and its provenance.
285. `PROOF` — compile it through the production Roslyn pipeline with only the permitted `.Contracts`
     reference set.
286. `PROOF` — the four negative compilation cases from 4.2, each failing loudly and creating no
     installable proposal.
287. `PROOF` — the recording pass: zero external calls, subscriptions, durable side effects or live
     facts; manifest exactly right; bound to digests.
288. `PROOF` — submit the proposal through the client API **as exercised through DigitalBrain MCP**.
289. `PROOF` — installing before approval fails.
290. **HUMAN GATE — present the exact source digest and manifest to the distinct human approver and
     wait.** This approval cannot be pre-authorized, simulated, or performed by the model identity.
     Automated tests may use a separately named test-human fixture principal, but the final live
     proposal goes to an actual human. **Stop here until it is supplied.**
291. `PROOF` — append the approval to the ledger; install the behavior.
292. `PROOF` — capture and compare silo PID and start time, loaded manifest and build artifacts
     before and after. Prove **no rebuild, no silo restart, no AppHost topology mutation, no
     FeatureHost, no collectible `AssemblyLoadContext`, no second runtime neuron kind.**
293. `PROOF` — send a real triggering fact; the behavior executes immediately.
294. `PROOF` — invoke a typed request; observe automatic reification on the caller's rail **without
     caller cooperation**.
295. `PROOF` — verify owner, actor, correlation, causation, caller, sequence, timestamp and cursor
     evidence.
296. `PROOF` — disconnect and reconnect MCP using its cursor; prove catch-up and stale-cursor reset
     **without server-side polling or `Task.Delay`**.
297. `PROOF` — revert the governance journal entry rather than deleting runtime state directly; prove
     rollback in the same running silo.
298. `PROOF` — restart the runtime resources deliberately; prove approvals, behavior state, rollback
     history, cursor behaviour and journal state reconstruct.
299. `GATE` — run the 4.8 benchmark against the real model.
300. `GATE` — root gate, background, polled, quoted.
301. `IMPL` — stop and clean up **only** resources this run started.
302. `READ` — verify final git status, and no leaked processes, ports, credentials or temporary
     artifacts.

## 7.6 — failure injection

303. `PROOF` — each of: Ollama unavailable; configured model missing; port 5000 collision; MCP
     disconnect and reconnect; duplicate command / idempotency key; cancellation and timeout;
     malformed and oversized requests; compile failure; tampered or stale approval; self-approval
     attempt; behavior exception; slow or unavailable receiver; concurrent proposal/install/rollback;
     resource restart; cursor expiry; storage failure where safely reproducible.
304. `READ` — for each: **no swallowed errors, no silent fallback, no duplicate installations or
     effects, no infinite internal retry, and no timeout encoded inside a successful JSON body.**
305. `IMPL` — on every live failure capture, with secrets redacted: git SHA and status; exact command
     and exit code; Aspire resource snapshot and health; structured logs; the distributed trace by
     correlation id; the MCP request and error; Ollama provider and model evidence; a bounded feed
     page and cursor; the relevant ledger slice; silo PID and start time.
306. `ADV` — final adversarial review.
307. `GATE` — full gate.
308. `IMPL` — **delete this file.** Its plan is consumed. That is what scaffolding is for.

---

## Completion standard

Not a percentage. All of:

- Every applicable §9 step implemented and evidenced.
- Every §11 prerequisite decided by its required point.
- DEC-1 … DEC-12 still true, or an explicit reversal recorded.
- Nothing §12 rejects has silently returned.
- All intended projects compiled by the solution and root gate.
- `PublicAPI` and alias baselines current.
- No added production code without a present consumer.
- Root gate green with its actual output in front of you.
- The live MCP/Ollama acceptance sequence passed.
- Model identity and benchmark evidence prove a real local model was used.
- The exact human-approved behavior installed without rebuild or restart.
- Typed calls automatically journaled.
- Journal-entry revert rolls the behavior back.
- Restart reconstruction proven.
- Worktree clean, every green slice committed.

