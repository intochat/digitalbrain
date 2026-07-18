# DigitalBrain Claude Code LSP plugin

This directory is a **local Claude Code plugin marketplace** that ships one plugin,
`digitalbrain-lsp`, which activates Claude Code's built-in `LSP` tool (go-to-definition,
find-references, hover, and instant post-edit diagnostics) for this repo's two
languages:

| Language | Server command (as Claude Code spawns it)                                   |
| -------- | --------------------------------------------------------------------------- |
| C#       | `dotnet tool run csharp-ls -- --solution ${CLAUDE_PROJECT_DIR}/DigitalBrain.slnx` |
| Dart     | `cmd /c dart language-server --client-id claude-code`                        |

## Why this exists (don't "just use the official plugin")

Claude Code only exposes the `LSP` tool when an **enabled code-intelligence
plugin** declares the servers. A bare project-root `.lsp.json` does **not**
activate it (verified empirically on v2.1.143). No off-the-shelf plugin fits
this repo:

- **C#** — the official `csharp-lsp` plugin expects a *global* `csharp-ls` on
  `PATH`. This repo deliberately pins `csharp-ls` as a **local dotnet tool**
  (`.config/dotnet-tools.json`, restored on first build by
  `Directory.Build.targets`). So we drive it via `dotnet tool run`, which keeps
  the pinned version and needs no global install.
- **Dart** — there is **no Dart plugin in any marketplace**, official or
  otherwise. It can only be done with a custom plugin like this one.

## v2.1.143 gotchas baked into the config (discovered by verification)

1. **Only `command` / `args` / `extensionToLanguage` are safe.** The docs list
   `restartOnCrash`, `maxRestarts`, `startupTimeout`, `shutdownTimeout`,
   `workspaceFolder` as supported, but this Claude Code version **hard-fails LSP
   initialization** if any of them are present (one error per field, in order).
   Keep the manifest minimal.
2. **Windows `.bat` won't spawn directly.** Claude Code launches LSP servers
   with libuv `uv_spawn`, which does not resolve `PATHEXT`. `dart` on `PATH` is
   `dart.bat` → `ENOENT`. Hence `command: "cmd", args: ["/c", "dart", ...]`.
   `dotnet` needs no wrapper because it's a real `.exe`. This config is
   Windows-targeted (the repo is Windows-only per `CLAUDE.md`); a
   macOS/Linux contributor must change the Dart entry to
   `command: "dart", args: ["language-server", ...]`.

## How it's wired

Installed at **project scope**, so it is declared in `.claude/settings.json`
(`enabledPlugins["digitalbrain-lsp@digitalbrain-local"]` + `extraKnownMarketplaces`):

```sh
claude plugin marketplace add ./tools/claude-lsp --scope project
claude plugin install digitalbrain-lsp@digitalbrain-local --scope project
```

A teammate cloning the repo gets the plugin once their Claude Code session
trusts the folder. **They still need the toolchain installed**: the .NET SDK
(restores `csharp-ls` on first `dotnet build`) and the Flutter/Dart SDK on
`PATH`.

> **Known portability caveat:** `claude plugin marketplace add` records an
> *absolute* path in `.claude/settings.json`
> (`extraKnownMarketplaces.digitalbrain-local.source.path`). Change it to the
> project-relative `tools/claude-lsp` so it resolves on every clone. (Editing
> `.claude/settings.json` is gated by the Claude Code permission classifier, so
> this change must be made/approved by a human.)

## After editing `digitalbrain-lsp/.claude-plugin/plugin.json`

`version` is pinned, so changes are not picked up until you bump it and refresh:

```sh
# bump "version" in digitalbrain-lsp/.claude-plugin/plugin.json, then:
claude plugin marketplace update digitalbrain-local
claude plugin update digitalbrain-lsp@digitalbrain-local --scope project
# restart Claude Code (or /reload-plugins) to apply
```

`claude plugin validate ./tools/claude-lsp/digitalbrain-lsp` checks the manifest.

## Verifying it works

A fresh headless session that actually calls the tool:

```sh
claude -p "Read src/core/DigitalBrain.Core/NeuronId.cs, then use the LSP tool to find references of NeuronId. Report the count."
```

Expected: the `LSP` tool is available and returns real references (e.g. ~68
across ~34 files for `NeuronId`). The debug log
(`claude --debug-file out.log ...`) should show
`LSP manager initialized with 2 servers` and
`LSP server plugin:digitalbrain-lsp:{csharp,dart} initialized` with no
`Failed to initialize LSP server` lines.

> Cold start: `csharp-ls` loads the whole `DigitalBrain.slnx` before answering the
> first request (tens of seconds to a few minutes on this solution). This is a
> `csharp-ls` characteristic, not a wiring fault — give the first LSP call time.
