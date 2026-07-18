# TripRadar

Travel platform with flight/hotel search, price tracking, Telegram bot notifications, and a React frontend — orchestrated by .NET Aspire.

## Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 11.0 Preview |
| Docker Desktop | Latest |
| Node.js | 22+ LTS |

### Install via winget (Windows)

```powershell
winget install Microsoft.DotNet.SDK.Preview
winget install Docker.DockerDesktop
winget install OpenJS.NodeJS.LTS
```

### Optional

```powershell
# Aspire CLI — for `aspire start` as an alternative to `dotnet run`
irm https://aspire.dev/install.ps1 | iex

# Update Aspire CLI to the latest version (if already installed)
aspire update --self

# Google Cloud CLI — required for Stitch MCP (AI-powered UI design)
winget install Google.CloudSDK
```

## Getting Started

```bash
git clone https://github.com/RoseXTechnology/TripRadar.git
cd TripRadar
dotnet run --project src/Aspire/Aspire.csproj
```

That's it. The first build automatically restores dotnet local tools and npm packages, then Aspire pulls all containers (PostgreSQL, Redis, Kafka, Flagd, Stripe CLI, Cloudflared), builds all projects, runs migrations, and opens the dashboard.

On first run the dashboard will prompt for external service secrets (Telegram bot token, Stripe key, etc.). Internal secrets (JWT, encryption keys) are auto-generated.

### Claude Code LSP (optional)

LSP servers for C# and TypeScript/JavaScript are configured in `.lsp.json` and provide Claude Code with real-time diagnostics, go-to-definition, and code intelligence. Both are automatically available after the first build — `csharp-ls` via dotnet local tools and `typescript-language-server` via npm devDependencies.

### Stitch MCP (UI Design)

The project uses [Stitch](https://stitch.withgoogle.com/) for AI-powered UI design via MCP. [Stitch agent skills](https://github.com/google-labs-code/stitch-skills) are already included in the repo (`.agents/skills/`). To connect the MCP server, authenticate with gcloud and run the Stitch init:

```bash
gcloud auth login
npx -y @_davideast/stitch-mcp init -c claude-code -t stdio
```

See [docs/stitch-mcp.md](docs/stitch-mcp.md) for the full setup guide.

## Maintenance

```bash
# Update Aspire CLI
aspire update --self

# Update all Stitch agent skills from upstream
npx skills add google-labs-code/stitch-skills --skill design-md --skill enhance-prompt --skill "react:components" --skill remotion --skill shadcn-ui --skill stitch-design --skill stitch-loop --skill taste-design -y

# Check for new skills available upstream
npx skills add google-labs-code/stitch-skills --list
```
