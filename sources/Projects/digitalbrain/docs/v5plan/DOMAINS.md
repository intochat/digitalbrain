# DOMAINS — the install model (v5)

> A **domain** is a public GitHub repo containing `.ino` files. A
> **brain** has its own list of installed domains. There is no central
> catalog, no marketplace gate, no service-side enumeration. `git clone`
> is the installer; `ls` is the registry.

---

## 1. Layout on disk

```
%LocalAppData%\DigitalBrain\
└── brains\
    ├── primary\                       # one folder per brain
    │   ├── brain.json                 # { id, name, createdAt }
    │   ├── domains\                   # installed domains
    │   │   ├── digitalbrain\
    │   │   │   ├── core\              # github.com/digitalbrain/core
    │   │   │   │   ├── *.ino          # neurons
    │   │   │   │   └── manifest.ino   # domain-level metadata
    │   │   │   ├── google\
    │   │   │   └── canvas\
    │   │   └── acme\
    │   │       └── reporting\         # user's private domain
    │   ├── generated\                 # neurons authored by Creator
    │   │   └── *.ino
    │   ├── state\                     # per-brain Orleans grain storage
    │   └── logs\
    └── acme-client\
        └── …
```

Two brains on one machine share the Orleans cluster but live in
disjoint folders. That's the V4-3 isolation invariant, made physical.

---

## 2. What a domain repo looks like

```
github.com/digitalbrain/google/
├── manifest.ino                    # domain-level declaration
├── Gmail.ino                       # one neuron per file
├── Sheets.ino
├── Calendar.ino
└── Drive.ino
```

```ino
# manifest.ino
domain Google
  "Google Workspace connectors."
  version 0.3.1
  requires digitalbrain/core >= 0.5

  # Platform-access neurons may ship a .cs sidecar (see SDK.md);
  # the manifest declares which ones do.
  uses-sdk Gmail
  uses-sdk Sheets
  uses-sdk Calendar
  uses-sdk Drive
```

`uses-sdk <Name>` means the neuron has a co-located `<Name>.cs`
implementation that the runtime will Roslyn-compile alongside the
`.ino`. Pure `.ino` neurons (no platform access) need no sidecar — the
runtime generates the whole `.cs`.

---

## 3. The install command

```pwsh
digitalbrain install digitalbrain/google
digitalbrain install digitalbrain/google@0.3.1   # pin a version
digitalbrain install ./local-domain-folder       # symlink for development
digitalbrain uninstall digitalbrain/google
digitalbrain list
```

Under the hood: `git clone --depth=1`. Versions are git tags. The
install is per-brain; switching brains switches the available domain
set. The CLI is a thin neuron — `DigitalBrain.Cli` — calling the
`BrainRegistry` and `DomainInstaller` neurons.

Inside the Brain Scene, the same operation runs through Ino:

> *"Install the Google domain."*
>
> Ino → Creator → `install digitalbrain/google` directive → user confirms
> → `git clone` runs → domain appears in the graph.

---

## 4. Resolution at activation (replaces MapCatalog)

When a neuron with `using mailbox = neuron(Google.Gmail)` activates:

1. **Walk the brain's domain set.** Enumerate
   `brains/{brainId}/domains/**/*.ino` plus the SDK's built-ins
   (`DigitalBrain.SDK` ships a few — `Ai.Chat`, `Windows.FileSystem`,
   `Sqlite`).
2. **Match by FQN.** `Google.Gmail` ⇒ find the `.ino` declaring
   `neuron Google.Gmail`. If multiple match (versioned shadowing), the
   highest-installed-version wins.
3. **If found:** the runtime warms the grain reference. The first
   `ask mailbox to ...` activates the target grain.
4. **If not found:** the activating neuron emits
   `Neuron.UnresolvedReference(missing: "Google.Gmail")` and parks
   itself with RFW lock = `modal`, showing the user a one-tap "Install"
   button.

There is **no global registry**. There is **no catalog cache**. The
filesystem is the source of truth. This is the same simplification AWS
made when S3 keys replaced filesystem hierarchies — flat, addressable,
no joins.

---

## 5. Discovery (the de-facto registry)

GitHub topic: **`digitalbrain-domain`**. The CLI's `digitalbrain search`
just runs:

```
GET https://api.github.com/search/repositories?q=topic:digitalbrain-domain
```

Sorted by stars. The user picks one. There is no curated list, no
"official" set beyond what `digitalbrain/*` (the org) ships. The
official-vs-community distinction is purely social.

A repo qualifies as a domain if its root has a `manifest.ino`. Any
other check (signing, scope review, malware scan) is the user's
responsibility, same as any other open-source dependency. v5
deliberately does not build a Trust Layer — that's premature
infrastructure.

---

## 6. Multi-brain isolation (V4-3 mechanism)

| Resource | Namespacing |
|---|---|
| Grain key | `{brainId}::{neuronFqn}` |
| Storage path | `brains/{brainId}/state/{neuronFqn}.json` |
| OAuth tokens | `brains/{brainId}/auth/{provider}.bin` (DPAPI-encrypted) |
| Installed domains | `brains/{brainId}/domains/` |
| Generated neurons | `brains/{brainId}/generated/` |
| Logs | `brains/{brainId}/logs/` |
| Telemetry meter tag | `brain.id={brainId}` |

The `{brainId}` prefix is enforced by `DigitalBrain.Runtime` at grain key
construction — no neuron sees a raw, unprefixed key. Switching the
camera between brains in the Constellation just switches the
`BrainId` claim on the Aspire-mounted Orleans client.

---

## 7. The official `digitalbrain/*` org (recommended starter set)

These are the only domains the Constellation pre-installs in a fresh
brain. Everything else is user-installed.

| Repo | Purpose |
|---|---|
| `digitalbrain/core` | `Ai.Chat`, `Ai.Embedding`, `Ai.Transcribe` (Microsoft.Extensions.AI wrappers) |
| `digitalbrain/windows` | `Windows.FileSystem`, `Windows.Process`, `Windows.Clipboard` |
| `digitalbrain/google` | `Google.Gmail`, `Google.Sheets`, `Google.Calendar`, `Google.Drive` |
| `digitalbrain/canvas` | `Canvas.Diagram`, `Canvas.Chart`, `Canvas.Image` |
| `digitalbrain/storage` | `Sqlite`, `Memory`, `Vector` |

Each of these is *also* just a GitHub repo. The user can fork, edit,
or replace any of them.

---

## 8. What is **not** in v5

- ❌ A central package registry (npm/nuget-style)
- ❌ Signed bundles
- ❌ A marketplace UI as a first-class shell surface (still planned for
  later — see v4 §10 — but not on the v5 critical path)
- ❌ Versioning beyond "latest git tag wins"
- ❌ Dependency resolution beyond `requires <other-domain> >= <version>`
- ❌ Sandboxing — domain `.cs` sidecars run with the same trust as the
  rest of the runtime; users vet what they install

When the product needs any of these, we'll add them. We do not pre-build
them.
