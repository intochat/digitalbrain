# AI Workflows & Configuration

This project uses Claude Code as the primary AI-assisted development tool. All configuration lives at project level so it travels with the repo.

## Configuration Files

| File | Purpose |
|------|---------|
| `.claude/settings.json` | Plugins, permissions, MCP server enablement |
| `.mcp.json` | MCP server definitions (commands, args, env vars) |
| `.lsp.json` | Language server configurations with project-specific args |
| `CLAUDE.md` | Project instructions, architecture overview, code style rules |

## MCP Servers

Defined in `.mcp.json`. These provide Claude with live tool access to external systems.

| Server | Purpose | Tools |
|--------|---------|-------|
| **aspire** | Aspire dashboard integration | List resources, logs, traces, metrics, execute commands (rebuild/restart/stop) |
| **context7** | Library documentation lookup | Resolve library IDs, query docs — used before writing any code |
| **microsoft-learn** | Official Microsoft docs | Search docs, fetch full pages, find code samples |
| **playwright** | Browser automation | Navigate, click, fill, screenshot, evaluate JS |
| **chrome-devtools** | Chrome DevTools Protocol | Network requests, console messages, performance traces, Lighthouse audits |
| **stitch** | UI design tool | Generate screens from text, apply design systems, edit screens |

The `episodic-memory` plugin also runs its own MCP server automatically (semantic search across past conversations).

## LSP Servers

Defined in `.lsp.json`. These give Claude real-time code intelligence (diagnostics, go-to-definition, completions).

| Server | Binary | Project-Specific Config |
|--------|--------|------------------------|
| **csharp** | `csharp-ls` (via `dotnet tool run`) | `--solution TripRadar.slnx` |
| **typescript** | `typescript-language-server` (via `npx`) | `--prefix src/TripRadar.WebUI`, `workspaceFolder: src/TripRadar.WebUI` |

Both are installed automatically on first build — `Directory.Build.targets` runs `dotnet tool restore` (installs `csharp-ls`), and Aspire runs `npm install` for WebUI (installs `typescript-language-server`).

The `csharp-lsp` and `typescript-lsp` plugins register these LSPs with Claude Code; the `.lsp.json` provides the project-specific arguments.

## Plugins

All enabled at project scope in `.claude/settings.json`.

### Core Development

| Plugin | Type | Activation | What It Does |
|--------|------|------------|-------------|
| **superpowers** | Skills | On-demand | Core skills library — TDD, debugging, planning, brainstorming (20+ skills) |
| **feature-dev** | Skill + Agents | On-demand (`/feature-dev`) | Guided feature development with codebase analysis and architecture focus |
| **frontend-design** | Skill | On-demand | Generates production-grade frontend interfaces |
| **code-simplifier** | Skill | On-demand | Reviews and simplifies recently changed code |

### Code Review & Quality

| Plugin | Type | Activation | What It Does |
|--------|------|------------|-------------|
| **code-review** | Skill + Agent | On-demand (`/code-review`) | Code review a pull request |
| **pr-review-toolkit** | 6 Agents | On-demand | Specialized reviewers: test gaps, silent failures, type design, comments, code quality |
| **security-guidance** | Hook (PreToolUse) | Automatic on every Edit/Write | Warns about XSS, injection, `eval()`, `innerHTML`, unsafe patterns |

### Git Workflow

| Plugin | Type | Activation | What It Does |
|--------|------|------------|-------------|
| **commit-commands** | Commands | On-demand (`/commit`, `/commit-push-pr`, `/clean_gone`) | Auto-generate commit messages, push + create PR in one step |

### Automation & Continuity

| Plugin | Type | Activation | What It Does |
|--------|------|------------|-------------|
| **double-shot-latte** | Hook (Stop) | Automatic when Claude tries to stop | Judge process decides whether to auto-continue or stop. Max 3 continuations per 5 min |
| **ralph-loop** | Hook (Stop) + Skill | On-demand (`/ralph-loop`) | Iterative while-true loop — feeds same prompt repeatedly until task completion |
| **hookify** | Hook (PreToolUse, PostToolUse, Prompt, Stop) | Automatic on every tool call | Checks user-defined rules in `.claude/hookify.*.local.md`. Zero cost if no rules defined |

### Memory & Management

| Plugin | Type | Activation | What It Does |
|--------|------|------------|-------------|
| **episodic-memory** | MCP + Hook (SessionStart) | Automatic — syncs on session start, MCP server always running | Semantic search across past conversations. Local SQLite + vector embeddings |
| **claude-md-management** | Skill | On-demand | Audit and improve CLAUDE.md files |

### Language Servers

| Plugin | Type | Activation | What It Does |
|--------|------|------------|-------------|
| **csharp-lsp** | LSP | Automatic | C# code intelligence — requires `.lsp.json` for project-specific config |
| **typescript-lsp** | LSP | Automatic | TypeScript/JS code intelligence — requires `.lsp.json` for project-specific config |

## Plugin Context Impact

Most plugins have zero context overhead until invoked. The always-active ones:

| Plugin | Always Active? | Context Cost |
|--------|---------------|-------------|
| **double-shot-latte** | Stop hook fires when Claude stops | Zero — runs external judge subprocess |
| **security-guidance** | PreToolUse hook on Edit/Write | ~3-30 lines per warning, doesn't repeat same warning per session |
| **hookify** | PreToolUse/PostToolUse/Prompt/Stop | Zero if no rules configured |
| **episodic-memory** | Background MCP server + SessionStart sync | Minimal — only returns results when search tool is invoked |
| **csharp-lsp / typescript-lsp** | LSP processes running | Zero context — provides diagnostics on demand |

## Verification Flow

After making changes, Claude follows this workflow (defined in `CLAUDE.md`):

1. **Build** — `dotnet build src/Aspire/Aspire.csproj`
2. **Start** — Verify all resources running via `mcp__aspire__list_resources`
3. **Simulate** — Use Playwright MCP to interact with the UI, take screenshots
4. **Verify telemetry** — Check traces, logs, and structured logs via Aspire MCP
5. **Return results** — Only after full flow passes

## Scope Model

Configuration has three scopes:

| Scope | Location | Shared? | Use For |
|-------|----------|---------|---------|
| **project** | `.claude/settings.json` | Yes (in git) | All project-specific plugins, permissions, MCP enablement |
| **local** | Auto-managed by Claude Code | No (gitignored) | Per-developer overrides (`.claude/settings.local.json`) |
| **user** | `~/.claude/settings.json` | No | Global personal prefs only (model, effort level, voice) |

This project keeps all tool configuration at project scope. User-level settings should only contain personal preferences like model choice and effort level.
