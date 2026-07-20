#Requires -Version 7
<#
.SYNOPSIS
  Install / sync multi-harness agent plugins and portable skills for this repo.

.PARAMETER Harness
  Which adapter(s) to set up: all, claude, grok, codex, skills
#>
param(
  [ValidateSet("all", "claude", "grok", "codex", "skills")]
  [string]$Harness = "all"
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$InventoryPath = Join-Path $PSScriptRoot "inventory.json"
$SkillsRoot = Join-Path $RepoRoot ".agents/skills"
$CacheOfficial = Join-Path $env:USERPROFILE ".claude/plugins/cache/claude-plugins-official"
$CacheSuperpowers = Join-Path $env:USERPROFILE ".claude/plugins/cache/superpowers-marketplace"
$MarketplaceOfficial = Join-Path $env:USERPROFILE ".claude/plugins/marketplaces/claude-plugins-official/plugins"

function Write-Step([string]$Message) { Write-Host "`n==> $Message" -ForegroundColor Cyan }

function Get-LatestPluginDir([string]$Name) {
  $base = Join-Path $CacheOfficial $Name
  if (-not (Test-Path $base)) { return $null }
  Get-ChildItem $base -Directory |
    Sort-Object { if ($_.Name -eq "unknown") { "0" } else { $_.Name } } -Descending |
    Select-Object -First 1 -ExpandProperty FullName
}

function Ensure-SkillFromDir([string]$Name, [string]$SrcDir) {
  if (-not (Test-Path $SrcDir)) { throw "Missing skill source dir: $SrcDir" }
  $dest = Join-Path $SkillsRoot $Name
  if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
  New-Item -ItemType Directory -Force -Path $dest | Out-Null
  Copy-Item -Recurse -Force (Join-Path $SrcDir "*") $dest
  if (-not (Test-Path (Join-Path $dest "SKILL.md"))) { throw "No SKILL.md: $Name" }
}

function Ensure-SkillFromMarkdown([string]$Name, [string]$SrcFile, [string]$Description) {
  if (-not (Test-Path $SrcFile)) { throw "Missing markdown: $SrcFile" }
  $dest = Join-Path $SkillsRoot $Name
  if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
  New-Item -ItemType Directory -Force -Path $dest | Out-Null
  $raw = Get-Content -Raw -Path $SrcFile
  $body = $raw
  if ($raw -match '(?s)^---\r?\n.*?\r?\n---\r?\n(.*)$') { $body = $Matches[1].TrimStart() }
  $desc = $Description
  if ($raw -match '(?m)^description:\s*(.+)$') { $desc = $Matches[1].Trim().Trim('"') }
  $skill = "---`nname: $Name`ndescription: $desc`n---`n`n$body"
  [System.IO.File]::WriteAllText((Join-Path $dest "SKILL.md"), $skill)
}

function Sync-PortableSkills {
  Write-Step "Sync portable skills into .agents/skills"
  New-Item -ItemType Directory -Force -Path $SkillsRoot | Out-Null

  $sp = Get-LatestPluginDir "superpowers"
  if ($sp) {
    Get-ChildItem (Join-Path $sp "skills") -Directory -ErrorAction SilentlyContinue | ForEach-Object {
      Ensure-SkillFromDir $_.Name $_.FullName
    }
  }

  $fd = Get-LatestPluginDir "frontend-design"
  if ($fd) { Ensure-SkillFromDir "frontend-design" (Join-Path $fd "skills/frontend-design") }

  $cr = Get-LatestPluginDir "code-review"
  if ($cr) {
    Ensure-SkillFromMarkdown "pr-code-review" (Join-Path $cr "commands/code-review.md") `
      "Code review a pull request with multi-agent confidence filtering."
  }

  $fdv = Get-LatestPluginDir "feature-dev"
  if ($fdv) {
    Ensure-SkillFromMarkdown "feature-dev" (Join-Path $fdv "commands/feature-dev.md") "Guided feature development."
    Ensure-SkillFromMarkdown "code-architect" (Join-Path $fdv "agents/code-architect.md") "Design feature architectures."
    Ensure-SkillFromMarkdown "code-explorer" (Join-Path $fdv "agents/code-explorer.md") "Analyze existing codebase features."
    Ensure-SkillFromMarkdown "feature-dev-code-reviewer" (Join-Path $fdv "agents/code-reviewer.md") "Feature-dev code reviewer."
  }

  $cs = Get-LatestPluginDir "code-simplifier"
  if ($cs) {
    Ensure-SkillFromMarkdown "code-simplifier" (Join-Path $cs "agents/code-simplifier.md") `
      "Simplify code for clarity while preserving functionality."
  }

  $rl = Get-LatestPluginDir "ralph-loop"
  if ($rl) {
    Ensure-SkillFromMarkdown "ralph-loop" (Join-Path $rl "commands/ralph-loop.md") "Iterative ralph-style implementation loop."
  }

  $cmdm = Get-LatestPluginDir "claude-md-management"
  if ($cmdm) {
    Ensure-SkillFromDir "claude-md-improver" (Join-Path $cmdm "skills/claude-md-improver")
    Ensure-SkillFromMarkdown "revise-claude-md" (Join-Path $cmdm "commands/revise-claude-md.md") "Revise CLAUDE.md from session learnings."
  }

  $prt = Get-LatestPluginDir "pr-review-toolkit"
  if ($prt) {
    Ensure-SkillFromMarkdown "review-pr" (Join-Path $prt "commands/review-pr.md") "Comprehensive PR review."
    Ensure-SkillFromMarkdown "pr-code-reviewer" (Join-Path $prt "agents/code-reviewer.md") "PR toolkit code reviewer."
    Ensure-SkillFromMarkdown "pr-code-simplifier" (Join-Path $prt "agents/code-simplifier.md") "PR toolkit simplifier."
    Ensure-SkillFromMarkdown "comment-analyzer" (Join-Path $prt "agents/comment-analyzer.md") "Analyze code comments."
    Ensure-SkillFromMarkdown "pr-test-analyzer" (Join-Path $prt "agents/pr-test-analyzer.md") "Review PR test coverage."
    Ensure-SkillFromMarkdown "silent-failure-hunter" (Join-Path $prt "agents/silent-failure-hunter.md") "Hunt silent failures."
    Ensure-SkillFromMarkdown "type-design-analyzer" (Join-Path $prt "agents/type-design-analyzer.md") "Analyze type design."
  }

  $cc = Get-LatestPluginDir "commit-commands"
  if ($cc) {
    Ensure-SkillFromMarkdown "commit" (Join-Path $cc "commands/commit.md") "Create a git commit."
    Ensure-SkillFromMarkdown "commit-push-pr" (Join-Path $cc "commands/commit-push-pr.md") "Commit, push, open PR."
    Ensure-SkillFromMarkdown "clean-gone" (Join-Path $cc "commands/clean_gone.md") "Clean gone remote branches."
  }

  $hk = Get-LatestPluginDir "hookify"
  if ($hk) {
    $wr = Join-Path $hk "skills/writing-rules"
    if (Test-Path $wr) { Ensure-SkillFromDir "writing-rules" $wr }
  }

  $epiSkill = Get-ChildItem (Join-Path $CacheSuperpowers "episodic-memory") -Recurse -Filter "SKILL.md" -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -eq "remembering-conversations" -or $_.Directory.Parent.Name -eq "skills" } |
    Select-Object -First 1
  if ($epiSkill) {
    Ensure-SkillFromDir $epiSkill.Directory.Name $epiSkill.Directory.FullName
  }

  # Matt Pocock: refresh from Codex user skills or Claude plugin cache if present
  $mattSrc = Join-Path $env:USERPROFILE ".codex/skills"
  $mattNames = @(
    "grill-me", "grilling", "grill-with-docs", "setup-matt-pocock-skills", "wayfinder",
    "to-spec", "to-tickets", "tdd", "implement", "prototype", "research", "domain-modeling",
    "codebase-design", "code-review", "diagnosing-bugs", "triage", "improve-codebase-architecture",
    "ask-matt", "resolving-merge-conflicts", "handoff", "teach", "writing-great-skills"
  )
  foreach ($n in $mattNames) {
    $from = Join-Path $mattSrc $n
    if (Test-Path $from) {
      Ensure-SkillFromDir $n $from
    }
  }

  $count = (Get-ChildItem $SkillsRoot -Directory).Count
  Write-Host "Portable skills present: $count"
}

function Setup-Claude {
  Write-Step "Claude: ensure marketplaces (settings.json is source of truth for enablement)"
  $settings = Join-Path $RepoRoot ".claude/settings.json"
  if (-not (Test-Path $settings)) { throw "Missing $settings" }
  Write-Host "Project settings: $settings"
  Write-Host "Install missing plugins inside Claude Code:"
  Write-Host "  /plugin marketplace add anthropics/claude-plugins-official"
  Write-Host "  /plugin marketplace add obra/superpowers-marketplace"
  Write-Host "  /plugin marketplace add mattpocock/skills"
  Write-Host "  then enable each id from inventory (or restart Claude to pick up settings.json)"
}

function Setup-Grok {
  Write-Step "Grok: install plugins"
  $installs = @(
    @{ Name = "superpowers"; Source = "obra/superpowers" },
    @{ Name = "mattpocock-skills"; Source = "mattpocock/skills" },
    @{ Name = "double-shot-latte"; Source = "obra/double-shot-latte" },
    @{ Name = "episodic-memory"; Source = "obra/episodic-memory" }
  )

  # Local official plugins when marketplace clone exists
  if (Test-Path $MarketplaceOfficial) {
    foreach ($p in @("frontend-design", "code-review", "feature-dev", "code-simplifier", "ralph-loop",
      "claude-md-management", "pr-review-toolkit", "commit-commands", "hookify", "security-guidance",
      "csharp-lsp", "typescript-lsp")) {
      $local = Join-Path $MarketplaceOfficial $p
      if (Test-Path $local) {
        $installs += @{ Name = $p; Source = $local }
      }
    }
  }

  foreach ($item in $installs) {
    Write-Host "Installing $($item.Name) from $($item.Source) ..."
    try {
      & grok plugin install $item.Source --trust 2>&1 | Out-Host
    } catch {
      Write-Warning "Install failed for $($item.Name): $_"
    }
  }

  $cfg = Join-Path $RepoRoot ".grok/config.toml"
  Write-Host "Project enable list: $cfg"
}

function Setup-Codex {
  Write-Step "Codex: enable superpowers plugin; skills come from .agents/skills"
  try {
    & codex plugin add superpowers@openai-curated --json 2>&1 | Out-Host
  } catch {
    Write-Warning "codex plugin add superpowers: $_"
  }
  $cfg = Join-Path $RepoRoot ".codex/config.toml"
  if (-not (Test-Path $cfg)) {
    Write-Warning "Missing $cfg — create project MCP config"
  } else {
    Write-Host "Project config: $cfg (requires trusted project)"
  }
}

Write-Host "Repo: $RepoRoot"
Write-Host "Harness: $Harness"
if (-not (Test-Path $InventoryPath)) { throw "Missing inventory: $InventoryPath" }

if ($Harness -in @("all", "skills")) { Sync-PortableSkills }
if ($Harness -in @("all", "claude")) { Setup-Claude }
if ($Harness -in @("all", "grok")) { Setup-Grok }
if ($Harness -in @("all", "codex")) { Setup-Codex }

Write-Step "Running verify"
& (Join-Path $PSScriptRoot "verify.ps1")
exit $LASTEXITCODE
