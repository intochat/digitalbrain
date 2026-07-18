# Stitch MCP

We use [Stitch](https://stitch.withgoogle.com/) as an AI-powered UI design tool via its MCP server. It enables coding agents (Claude Code, Cursor, Gemini CLI) to generate, iterate on, and export high-fidelity UI screens directly from natural language prompts.

The MCP server is configured in `.mcp.json` and requires a Stitch API key.

## Setup

### 1. Get a Stitch API key

Go to [stitch.withgoogle.com/settings](https://stitch.withgoogle.com/settings) and generate a persistent API key.

### 2. Set the API key as an environment variable

The `.mcp.json` references `${STITCH_API_KEY}` from your environment. Set it permanently so it persists across sessions:

```powershell
# Windows
setx STITCH_API_KEY "your-api-key-here"
```

```bash
# macOS/Linux — add to your shell profile (~/.bashrc, ~/.zshrc, etc.)
export STITCH_API_KEY="your-api-key-here"
```

Restart your terminal (or Claude Code) after setting the variable.

### 3. Verify

Restart Claude Code and check that the `stitch` MCP server shows as connected (`/mcp`).

## Stitch Skills

Agent skills from [google-labs-code/stitch-skills](https://github.com/google-labs-code/stitch-skills) are included in the repo under `.agents/skills/`. These are automatically picked up by coding agents (Claude Code, Gemini CLI, Cursor, etc.) — no manual installation needed.

| Skill | Purpose |
|-------|---------|
| `stitch-design` | Unified design workflow — prompt enhancement + screen generation |
| `stitch-loop` | Multi-page website generation from a single prompt |
| `design-md` | Design system documentation generation |
| `enhance-prompt` | Transforms vague UI ideas into optimized Stitch prompts |
| `react-components` | Converts Stitch designs to React component systems |
| `remotion` | Creates walkthrough videos from projects |
| `shadcn-ui` | Guidance for shadcn/ui component integration |
| `taste-design` | Semantic design system with premium anti-generic UI standards |

### Updating Skills

Update all skills from upstream in one command:

```bash
npx skills add google-labs-code/stitch-skills --skill design-md --skill enhance-prompt --skill "react:components" --skill remotion --skill shadcn-ui --skill stitch-design --skill stitch-loop --skill taste-design -y
```

Check for new skills available upstream:

```bash
npx skills add google-labs-code/stitch-skills --list
```
