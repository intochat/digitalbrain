# OS-ON-YAML Spec v0 (Formal)

**Status:** Y1 artifact. Extracted and formalized from OS-ON-YAML-PLAN.md §3 + appendices. This is the binding contract for implementation. Implementation (Y2+) MUST follow exactly. Changes to this spec require owner + updated PLAN handoff.

**Date:** 2026-06-15
**Owner:** Vlad
**Related:** docs/OS-ON-YAML-PLAN.md (full plan, rituals, stages), INOLANG-RFC.md (A default, B frozen, D escape, two-doors, HA YAML precedent), OS-FROM-INO-PLAN.md (rung-A headers, ParseBoot, os/ canonical, N±1, grants), DISTRIBUTION.md (capsule format, Packager SHA on payload, ContractDeclaration), VISION.md Core Law 1.

**Invariants (non-negotiable):**
- Parallel to os/ + .ino. No breakage to existing boot, capsules, hashes, DistributionDynamicHandlers core scenarios, ListSubscribers arithmetic, collector roundtrips, traceability audits.
- Produces **identical runtime observable effects** (contracts, RuleSet, UiSurface with UiWidget union, placements, grants, BootManifestApplied semantics) as current .ino path.
- All new C# (parsers, mappers, state) requires **Context7 (dnx dotnet-inspect) before the first line** + latest packages only (after lookup, via Directory.Packages.props).
- High-severity gate (`simulation.cs -- "Distribution" --ci`) 0 core failures after every logical change. Full aspire rituals (`aspire do build`, `aspire start`/`wait` on relevant resources, describe/logs before/after AppHost or manifest deltas).
- Ser discipline: append-only [Id(n)], concrete arrays only (no IReadOnlyList from collection expr).
- D2 freeze respected: no new rule statement kinds. Schemas enhance A (declarative contracts + shapes) and provide structured form for existing B sugar.
- Hashed text payload remains the source of truth for capsules (contentHash = SHA256 of canonical YAML text). Stable canonicalization required.
- Two-doors authoring preserved/extended: LLM targets schema (JSON/YAML under constraint), kernel or packager renders canonical YAML text.

---

## 1. Scope & Goals
Define a YAML-based, schema-first representation (`os-on-yaml/*.yaml`) that makes the **neurons and synapses paradigm** (VISION Law 1) explicitly declarable:
- **Synapse schemas**: field names, types, optionality (enables strong validation, template safety, future binder/codegen).
- **Schema: Neuron**: the "neuron contract" (id, grainType, handles with attached field contracts, emits, placement, requires, rules as data, UI surface contracts).
- Structured replacement for .ino rule + show card sugar (native YAML nesting + anchors for DRY; eliminates most custom InlineParser debt for widgets).
- Same AST lowering target as InoExperience / BootManifest / RuleSet so RuleHostNeuron, ShellNeuron, Packager, Marketplace, boot lowering, evidence replay, N±1, grants, etc. are unaffected or minimally dual-wired.
- Additional: anchors/aliases, standard validation (JSON Schema friendly) via schemas/os-on-yaml-v0.json (the living definition of the v0 vocabulary for this schemaVersion), better positions in diagnostics, interop, improved LLM target.

**Out of scope for v0** (per PLAN decisions):
- Replacing core C# Synapse records / Neuron base / UiWidget union / SourceGen (those remain the executable truth; schemas describe + validate + author).
- New B interpreter statements.
- Auto-dependency solver (requires: + surfaces only).
- Full dynamic "schema-defined grains" (L2/L3 escape remains for complex impl).
- Immediate unification (os/ + .ino stay canonical for shipping until later gated phase).

---

## 2. File & Capsule Format
- Source: `os-on-yaml/<id>.yaml` (parallel to `os/<id>.ino`).
- In capsule (`.brain` zip, same as today per DISTRIBUTION):
  - For full experience: manifest.json + `experience.yaml` (the canonical YAML text payload).
  - For contract-only: manifest.json + `contract.yaml` (or contract.json for compat; richer schema form allowed).
- contentHash = SHA256(UTF8(canonical YAML text)). Packager computes on the YAML text (generalized from .ino path).
- Manifest fields extended (backward compat, defaults false):
  - `HasYaml` (or reuse/extend HasRules + new `SchemaVersion`).
  - `ContractHandlers` (from schema handles/emits, same ContractDeclaration shape).
  - `DefaultRegion`, `DefaultPinned`, `DefaultOrder`, `Requires`, `IsSystem`, `RequiresGrant` (from neuron section).
- Detection in Packager (generalized): if content contains `schemaVersion: "os-on-yaml/` or top-level `neuron:` / `boot:`, treat as YAML path. Fallback to .ino heuristics for compat. `PackAsync(..., yamlContent: string)` overload or flag.

---

## 3. YAML Grammar v0 (precise, append-only)

All files are valid YAML 1.2 documents. Use block style for canonical output (stable key order: schemaVersion, neuron/boot, handles, emits, rules, synapses, scenarios, escalate at end where applicable). Comments `#` allowed. Anchors `&foo` / aliases `*foo` supported (for shared content trees).

### 3.1 Experience / Neuron capsule (maps primarily to InoExperience + schema extensions)
```yaml
schemaVersion: "os-on-yaml/v0"          # required, append-only vocabulary. The authoritative machine-readable definition lives at schemas/os-on-yaml-v0.json (JSON Schema draft 2020-12, usable by yaml-language-server, IDEs, LLM structured generation, and future Validate).
neuron:                                 # "Schema: Neuron" — the contract
  id: weather-watcher                   # -> InoExperience.Name
  grainType: weather-watcher            # for activation / GrainType (informational for L1; required for L0 schemas)
  version: 0.2.0                        # -> InoExperience.Version
  desc: "..."                           # optional
  handles:                              # -> triggers + ContractDeclaration (IsHandle=true)
    - synapse: WeatherQuery
      fields:                           # Synapse field contract (new; for validation + $subs safety)
        - name: City
          type: string
          required: true
  emits: [WeatherResult, UiSurface]     # -> emits + ContractDeclaration (IsHandle=false)
  region: widgets
  pinned: true
  order: 2
  requires: []
  requiresGrant: []
  observedSynapses: 0
  rules:                                # structured form of current on:/emit/show (maps 1:1 to RuleDeclaration / EmitRuleStatement / ShowCardRuleStatement + CardItem)
    - on: WeatherResult
      as: result
      do:
        - emit:
            type: WeatherResult
            args: { city: "{result.City}", summary: "see surface" }
        - show:
            card:
              title: "Weather — {result.City}"
              content:                  # array of CardItem (kinds: text|button|column|row|container|windowframe|divider|icon|textfield|progress|toggle|image)
                - kind: text
                  text: "$summary"
                - kind: button
                  label: "Refresh"
                  action:
                    type: WeatherQuery
                    args: { city: "{result.City}" }
  synapses:                             # reusable Synapse schemas (the "schema" part of "Schema: Neuron/Synapse")
    - name: WeatherResult
      fields:
        - name: city
          type: string
        - name: summary
          type: string
        - name: sourceUrl
          type: string?
  escalate: codegen WeatherWatcherNeuron   # optional, D escape (warning INO005 / YIN005)
```

Top-level `boot:` instead of `neuron:` for brain.yaml (maps to BootManifest; same fields as current brain.ino ParseBoot: llms list, voice, durability, ui, discovery, advertisedIpEnv, seeds list of .yaml/.ino paths, worlds list).

### 3.2 Rule / UI structures (exact mapping targets)
- `rules[]` → `RuleDeclaration[]` (On, Alias, When? {Field, Op, Value}, Do: RuleStatement[])
  - `emit: { type, args: {k: v template or literal} }` → EmitRuleStatement(EmitDescriptor)
  - `show: { card: { title?, content: CardItem[] } }` → ShowCardRuleStatement
- `CardItem` (kind + text + action? + children?):
  - kind: text | button | column | row | container | windowframe | divider | icon | textfield | progress | toggle | image
  - For button/action: `action: { type: SynapseName, args: {...} }`
  - Children arrays for column/row/container/windowframe (recursive).
- Templates: `{alias.Field}` or `$Field` (case-insensitive, same substitution as current RuleInterpreter.EvalTemplate + Substitute in RuleHost).
- Conditions (when present): same ops (== != contains startsWith > <).

### 3.3 Scenarios (for evidence/replay/L2 smoke)
Same Gherkin spirit as current inside .ino:
```yaml
scenarios:
  - name: "..."
    when: { emit: Foo, args: {..} }
    then:
      - broadcast: Bar
        with: { field: value }
      - broadcast: UiSurface
```
Reused by ReplayObservedSynapsesAsync (manifest.ObservedSynapses or declared).

### 3.4 Canonical form rules (for stable hash)
- Block style, 2-space indent.
- Keys in this order (per top-level): schemaVersion, neuron|boot, handles, emits, region/pinned/order/requires/requiresGrant/observedSynapses, rules, synapses, scenarios, escalate.
- Inside card content: kind first, then text/label/children/action as applicable.
- No trailing newlines beyond single \n at end of file for the payload.
- Strings quoted only when necessary (numbers/bools unquoted where unambiguous).
- The emitter (Yaml canonicalizer) must be tested for byte-stability on roundtrip (YAML → AST → YAML).

---

## 4. AST / Lowering / Runtime Contract
- New (or unified) loader in `DigitalBrain.Core.Domain.Yaml` produces:
  - Same `InoExperience` (Name, Version, Description, Emits, Rules: RuleDeclaration[], HasEscalateCodegen, DefaultRegion/Pinned/Order, Requires, IsSystem, RequiresGrant) for the rule/UI path.
  - Or extended `SchemaInoExperience : InoExperience` with additional `SynapseSchemas[]`, `NeuronContract` (grainType, handles with field maps).
- Boot: `BootManifest` (or extended) from `boot:` section. Same lowering in Sdk (ApplyLlms, seeds, worlds) and ino.cs shim.
- Contracts: From handles/emits + rules (on=handle for IRuleHostNeuron or declared neuron; emits) → same `ContractDeclaration` array contributed on install.
- Validation (`ValidateIno` or new `ValidateYaml`):
  - YIN001: syntax / required fields / schemaVersion (now also enforces the exact supported value from CurrentSchemaVersion and the JSON schema at schemas/os-on-yaml-v0.json; unknown schemaVersion produces explicit "does not exist" diagnostic).
  - YIN002: unknown synapse in handles/emits/rules (against DispatchManifest + installed contracts + declared synapses: in this file).
  - YIN003: unknown field on a declared/known synapse (now uses the inline or referenced synapse.fields).
  - YIN004: privileged emit not declared in requiresGrant + system context.
  - YIN005: escalate warning.
  - YIN006: manifest mismatch (neuron.id vs rules).
  - Stronger field contract checks for templates/conditions/args.
- Rule execution / UI lowering: unchanged (RuleInterpreter + SynapseBinder + RuleHost.BuildWidgets). The YAML content just feeds the same AST.
- Binder: May be extended later with schema for known types (still reflection fallback for dynamic).
- Packager: accepts yamlContent, detects, parses via new loader for Has* + decls, SHA on the yaml text, stores as experience.yaml entry.
- Marketplace / Installed views / ino tools: surface schema info when HasYaml (future; v0 tolerant).

Error exceptions: `InoParseException` reused/extended with YIN codes + line (YAML parsers provide good positions).

---

## 5. Boot & Hosting Implications
- `brain.yaml` (or `brain.ino` primary for compat) parsed by new boot loader (or dual in `InoParser.ParseBootYaml` or separate `YamlBootParser`).
- Sdk `AddDefaultDigitalBrainTopology` / `AddDigitalBrainManifest` + `DigitalBrainInoHost.Run` (ino.cs) generalized to check for brain.yaml and use equivalent BootManifest.
- Seeds can reference `os-on-yaml/*.yaml` (or mixed during transition).
- `BootManifestApplied` hash is of the canonical YAML text (or .ino).
- Diagnostics: line-numbered YIN/BOOT codes in terminal on parse fail (same as today).

---

## 6. Two-Doors Authoring & LLM
- Primary generation target: schema form (JSON or YAML under structured output / constrained decoding). Kernel/packager renders canonical YAML text for the hashed payload.
- Creator tab: Validate (using schema contracts) + edit YAML (or form over schema) + pack.
- LlmAgent author_experience tool: updated to emit schema-structured (not just templated .ino reminder).

---

## 7. Compatibility & Dual Support (mandatory for v0)
- Old path (`.ino` content, InoParser, experience.ino entry) 100% unchanged and passes all gates.
- New path (YAML content, Yaml loader, experience.yaml entry) produces **identical**:
  - Contract growth (ListSubscribers +1 per declared).
  - Rule execution + emitted synapses (including UiSurface with correct widgets).
  - Placement application.
  - Grant enforcement.
  - Replay report (observed → matched + produced).
  - Hash verification on install.
- Detection heuristics in Packager generalized (non-breaking).
- Capsule consumers (DigitalBrainGrain.InstallBundle) handle either entry.
- Tests: existing scenarios run against .ino fixtures; add parallel "yaml variant" scenarios that pack yamlContent and assert same N+1 / surface content / journal effects (tolerant where needed to protect gate).

---

## 8. Error Codes (new/ extended)
YIN001 syntax/required/schemaVersion/missing name+version.
YIN002 unknown synapse (declared or in DispatchManifest).
YIN003 unknown field on declared/known synapse.
YIN004 privileged/undeclared emit.
YIN005 escalate (warning).
YIN006 mismatch (id vs rules etc.).
YIN007 privileged directive in non-system schema.
YIN0xx-W warnings (no scenarios, unused alias, etc.).

Boot errors remain BOOT00x (or new YBOOT if in yaml path) with line numbers.

---

## 9. Implementation Constraints (from PLAN)
- New code under `src/DigitalBrain.Core/Domain/Yaml/` (YamlParser.cs, YamlAst.cs or reuse/extend Ino*, YamlValidator.cs, mappers to Ino*).
- Use standard latest YAML lib (YamlDotNet after Context7 confirmation of DeserializerBuilder/SerializerBuilder + stable emit config + naming).
- All new serializable types: `[GenerateSerializer]`, sequential [Id], concrete arrays.
- No changes to `InoParser.cs`, `InoAst.cs`, core rule interpreter, UiWidget union, Neuron base, SourceGen, or existing .ino examples unless explicitly additive + gated.
- Examples in `os-on-yaml/` must be roundtrippable and produce identical collector/Ui render / contract math as their .ino twins.
- Full handoff notes + ritual checklist in every commit (see PLAN §8 + Appendix C).

---

## 10. Open for Later Phases (not v0)
- Schema-driven C# codegen of Synapse records + IHandle skeletons (behind L2 quarantine + sim gate).
- Visual designer emitting the YAML.
- Full unification (make os-on-yaml/ the source for all, deprecate .ino syntax while keeping runtime compat, update brain.ino shim + all docs).
- Richer schema features (unions declared in YAML, ser Id assignment in schema for generated types, cross-file $ref for shared synapses).

---

**This spec is now the implementation contract.** Any deviation must be recorded in an updated PLAN with owner note + gate.

Next per user/PLAN: after this spec, begin Y2 (os-on-yaml/ examples + stub loader) with fresh Context7 calls, high-sev baseline, aspire smoke, then small deltas + re-gates.

(End of formal spec.)
