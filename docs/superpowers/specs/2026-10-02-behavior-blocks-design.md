# App authoring through behavior blocks

Status: interaction direction approved on 2026-10-02; written specification awaiting review.
Related issue: https://github.com/intochat/digitalbrain/issues/124

## Intent

An app is presented as a scrollable collection of small behaviors described in natural language. A person reads and edits each behavior independently, sees registry-backed neuron and signal references in context, and opens implementation details through its overflow menu. Verification scenarios expand beneath the behavior they describe. The user approved this direction after supplying a sketch of two simple blocks with three-dot menus. The final light-theme mockup is visual guidance, not literal test data or an architectural contract.

The product must remain simple: no IDE file tree, line numbers, permanent inspector, wizard, or conversation pane on this surface. Do not reproduce the large whitespace or incidental scenario counts in the generated mockup. Blocks fit their content. Preserve the shell theme and support both light and dark appearances.

## Scope and boundaries

This work replaces the flat specification presentation and introduces structured block authoring. It includes registry highlighting, source inspection, editable descriptions, explicit scenario links, and reliable verification presentation. It does not create scenario neurons, introduce a step grammar, change signal execution order, or replace the existing test gate.

Private installation before publication, marketplace redesign, and app sharing policy are separate work. Today AppDraftNeuron.Build calls publishing.Publish. Until that is changed deliberately, the draft action must say Build and publish and explain its effect. Do not show a Private badge copied from an earlier concept. Existing app installation and Open behavior remain authoritative.

## Main experience

The app title sits above a single column of behavior blocks. The header offers the applicable build/check action and Open app only for an installed app that supports opening. Source revision and verification time are available in a compact verification detail rather than competing with the title.

Each block shows a title, free-form description, overflow button, and a quiet verification summary. Registry tokens are highlighted inline without turning every occurrence into a large chip. Unknown prose remains ordinary text. The number of linked scenarios is a disclosure control. Expanded scenarios show their name, readable body, exact verdict, and selectable failure message. Live documentation has its own neutral label and does not count as passed. Shared scenarios can appear under several behaviors, but app-wide totals count each scenario once.

The overflow menu offers Edit description, View source, View scenarios, Duplicate, and Delete where the user has edit access. View source opens a dismissible sheet with the exact source file or files from the selected revision; it is read-only in this scope. Missing or not-yet-generated source has an explicit empty state. Published content is read-only; changes are made in a draft, with the source revision recorded. Editing creates pending changes and does not mutate the installed revision.

Edit description opens a focused title/description editor with Save and Cancel. Saving is a persistent draft edit and marks results as stale. Add behavior creates an independently editable block. Duplication creates a new identity and copies prose only; source links and scenario links are empty so it cannot silently duplicate executable subscriptions or claim existing verification. Delete removes the draft block after describing affected links; shared scenarios survive. All changes remain pending until a successful build produces a new revision.

Block display order is presentation order only. Signals and behavior code determine execution. Do not offer drag-to-order as a way of programming a sequence.

## Structured authoring model

Store an ordered collection of behavior records and a separate collection of scenario records in the existing draft state, not separate grains. A behavior has a stable opaque ID, title, natural-language description, source-path references, and scenario-ID references. A scenario has a stable opaque ID, exact gate name, body, and live-documentation flag. One scenario can refer to several behaviors through their explicit links. Unlinked scenarios appear in an app-level scenarios section so content is never hidden.

IDs survive renaming and reordering. Names remain the exact-string key used by the existing verification gate; stable IDs do not alter dbtest output matching. Reject duplicate gate names in newly authored structured content with an actionable error. Display legacy duplicate names without assigning an ambiguous passing verdict.

Draft mutations carry the expected draft revision. Stale writes return a conflict and retain unsaved editor text for recovery. Scope requests through the existing authenticated owner/brain rules; hiding a menu is not authorization. Append serialized fields and mirror wire changes in Dart; preserve existing aliases and numeric IDs. Use concrete arrays for persisted collections.

For new structured drafts, behavior/scenario records are the authoring source of truth. Generate app.spec.md deterministically for the existing builder and verification pipeline; it contains the preamble and each scenario exactly once. Persist a versioned authoring-content sidecar in PackageContent.Files containing the records and links so published revisions, forks, and reopened drafts round-trip the same identities. This is document metadata, not a new runtime manifest or execution model. Unknown metadata versions remain readable through the spec fallback and cannot be edited destructively.

Source paths refer to exact package content, never arbitrary host paths. They can be empty before a build and must resolve before a built block is advertised as having source. A behavior may reference more than one implementation file; do not force a one-to-one mapping or split the shipped research loop simply for visual symmetry. The existing PackageContent.Programs implementation continues to define executable files.

Author and Builder prompts produce/update structured records and explicit source associations. Validate their output as data. Do not infer links from lexical highlighting or silently invent mappings for existing packages. Generated prose is never interpreted at runtime. Only the existing compiled behaviors run.

## Existing content and migration

Existing packages without structured metadata continue to render their complete spec through the tolerant scenario parser required by issue 124. Preserve preambles, malformed sections, and arbitrary Markdown. Existing specs remain byte-for-byte unchanged during read-only viewing. A small explanation identifies the legacy document view without presenting it as a failure.

Converting existing content is an explicit draft operation: preserve original text, assign stable scenario IDs, and propose behavior groupings for review. Uncertain links remain unassigned. Conversion must not change execution or fabricate source ownership. The person can use the legacy view until conversion is accepted. Fixtures for Customer Researcher must retain its actual source structure and scenario meanings, rather than the illustrative mockup's three behaviors and counts.

## Registry and highlighting

Provide read-only vocabulary from the same composed-contract discovery boundary used by ScriptContracts. Expose it through a module contract; Apps must not reference CSharp implementation types. Reuse the platform-assembly and PlatformOnly exclusions for neuron contracts and apply equivalent exclusions to signal discovery. Return fully qualified identities, display names, kinds, owning modules, and available descriptions. Manifest operations/settings/accounts join this vocabulary. Reflection does not create new registry entries from arbitrary prose.

Neuron and signal identifier matching is case-sensitive and respects identifier boundaries. Manifest names use whole-word matching; quoted literals have their own visual treatment. Longest-match resolution prevents partial identifier highlights. A manifest token inside quotes retains its token kind rather than becoming an ordinary literal. Duplicate short names remain ambiguous: a tap lists qualified candidates, and the UI does not invent a resolved identity. Lexical source usage may prioritize candidates but does not prove scenario-to-code binding.

A token tap opens a small contextual detail with kind, qualified name, module, and description if available. Missing registry data degrades to ordinary prose plus manifest/literal highlighting. Tokens are advisory references; their color never proves verification. Matching is a pure Dart function independent of rendering and network access.

## Verification and lifecycle truth

Keep the existing full-app deterministic gate. The UI does not offer isolated scenario execution unless a real backend contract exists. Run checks verifies the selected immutable revision through the existing endpoint. Build and publish is a separate draft action with the current publishing semantics.

Results belong to an exact built revision. Any draft edit makes old results historical until a new build/check establishes current evidence. Expanding history can show prior verdicts, clearly labeled. A behavior summary is passed only when it has deterministic linked scenarios, all have passing exact-name results for the displayed revision, and the relevant run succeeded. Zero linked scenarios means No checks; only live scenarios means Live documentation. Missing results mean Not run, not passed. Overall build/runtime failures remain visible even if individual emitted scenario results passed.

Do not imply a check is running merely because a draft is being saved. Busy/error states belong to the affected action or block. Loading a registry detail must not disable scrolling or reading other blocks. Preserve edits on failures and show actionable retry paths. Do not expose stack traces as the primary error message.

## Flutter structure and accessibility

Use one shared behavior/scenario presentation across draft creation and app details. Separate typed document models, tolerant legacy parser, vocabulary matcher, source inspector, and block widgets. Replace dynamic-map interpretation within individual widgets with typed adapters at the API boundary.

Use a lazy scrolling list, content-sized blocks, selectable prose/failures, semantic overflow labels, keyboard-operable menus and disclosures, visible focus, and text/icon verdicts in addition to color. On narrow screens, source and vocabulary details open as full-width sheets. Avoid nested scrolling for ordinary scenario expansion. Color tokens must remain readable in both themes.

## Verification and acceptance

- A new draft has separately editable behavior blocks whose identities and links survive save, reopen, build, and package round-trip.
- No block edit changes an installed revision or runs new subscriptions before the normal build/install path.
- One scenario can verify multiple behaviors without duplicate app-wide counts; unlinked and malformed content is never hidden.
- View source shows exact selected-revision content and handles missing source without guessing.
- Registry neurons/signals highlight in prose; ordinary words do not. Ambiguous names stay ambiguous. Platform-only types never appear in vocabulary responses.
- Edits invalidate displayed current verdicts; zero checks, missing verdicts, live documentation, failures, and historical success remain distinct.
- Backend tests cover structured serialization, revision conflicts, authorization, validation, deterministic export, and revision/source consistency. Matcher/parser tests cover malformed input, boundaries, overlaps, quotes, ambiguity, and legacy documents.
- Widget tests cover menu actions, editing recovery, scenario expansion, shared counts, source details, and semantics in light/dark themes. Run flutter analyze and flutter test; run affected Apps and CSharp unit/E2E suites for changed contracts and discovery.
- Visually inspect the actual Flutter flow against the approved sketch/refinement on desktop and narrow widths before calling the implementation complete.

## Delivery order

Implement as reviewable increments: structured document/contracts and legacy adapter; registry vocabulary; shared Flutter blocks and disclosures; draft mutations and Author/Builder integration; complete end-to-end verification. These are implementation-plan boundaries, not permission to ship misleading stub controls. The written implementation plan follows review of this specification.
