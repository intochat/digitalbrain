#Requires -Version 7
<#
.SYNOPSIS
  Verify multi-harness capability parity from tools/harness/inventory.json.
  Exit 0 only when every capability is ok or explicitly unsupported per harness.
#>
param(
  [ValidateSet("all", "claude", "grok", "codex")]
  [string]$Harness = "all"
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$InventoryPath = Join-Path $PSScriptRoot "inventory.json"
$SkillsRoot = Join-Path $RepoRoot ".agents/skills"
$ClaudeSettings = Join-Path $RepoRoot ".claude/settings.json"
$GrokConfig = Join-Path $RepoRoot ".grok/config.toml"

$inventory = Get-Content -Raw -Path $InventoryPath | ConvertFrom-Json
$failures = [System.Collections.Generic.List[string]]::new()
$rows = [System.Collections.Generic.List[object]]::new()

function Test-SkillPath([string]$RelOrName) {
  if ([string]::IsNullOrWhiteSpace($RelOrName)) { return $false }
  $p = $RelOrName
  if ($p.StartsWith(".agents/")) {
    $full = Join-Path $RepoRoot ($p -replace "/", [IO.Path]::DirectorySeparatorChar)
  } else {
    $full = Join-Path $SkillsRoot $p
  }
  $skillMd = if ((Test-Path $full) -and (Get-Item $full).PSIsContainer) {
    Join-Path $full "SKILL.md"
  } else {
    $full
  }
  return (Test-Path $skillMd)
}

function Test-AnyPortableSkills([string[]]$Names) {
  if (-not $Names -or $Names.Count -eq 0) { return $false }
  foreach ($n in $Names) {
    if (Test-SkillPath $n) { return $true }
  }
  return $false
}

$claudeEnabled = @{}
if (Test-Path $ClaudeSettings) {
  $cs = Get-Content -Raw $ClaudeSettings | ConvertFrom-Json
  if ($cs.enabledPlugins) {
    $cs.enabledPlugins.PSObject.Properties | ForEach-Object { $claudeEnabled[$_.Name] = [bool]$_.Value }
  }
}

$grokEnabled = @()
if (Test-Path $GrokConfig) {
  $raw = Get-Content -Raw $GrokConfig
  if ($raw -match '(?s)enabled\s*=\s*\[(.*?)\]') {
    $grokEnabled = [regex]::Matches($Matches[1], '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
  }
}

$codexSuperpowers = $false
try {
  $list = & codex plugin list 2>$null | Out-String
  if ($list -match "superpowers@openai-curated\s+installed,\s+enabled") { $codexSuperpowers = $true }
} catch { }

function Check-Claude($cap) {
  $mode = $cap.claude.mode
  if ($mode -eq "unsupported") { return "unsupported" }
  if ($mode -eq "plugin") {
    $id = $cap.claude.id
    if ($claudeEnabled.ContainsKey($id) -and $claudeEnabled[$id]) { return "ok" }
    # skill fallback still counts for capability if portable skills exist
    if (Test-AnyPortableSkills @($cap.portable_skills)) { return "ok-skill-fallback" }
    return "missing-plugin:$id"
  }
  if ($mode -eq "skill") {
    if (Test-SkillPath $cap.claude.path) { return "ok" }
    if (Test-AnyPortableSkills @($cap.portable_skills)) { return "ok" }
    return "missing-skill"
  }
  return "unknown-mode:$mode"
}

function Check-Grok($cap) {
  $mode = $cap.grok.mode
  if ($mode -eq "unsupported") { return "unsupported" }
  if ($mode -eq "plugin") {
    $name = $cap.grok.name
    if ($grokEnabled -contains $name) { return "ok" }
    if (Test-AnyPortableSkills @($cap.portable_skills)) { return "ok-skill-fallback" }
    return "missing-plugin:$name"
  }
  if ($mode -eq "skill") {
    if ($cap.grok.path -and (Test-SkillPath $cap.grok.path)) { return "ok" }
    if (Test-AnyPortableSkills @($cap.portable_skills)) { return "ok" }
    return "missing-skill"
  }
  return "unknown-mode:$mode"
}

function Check-Codex($cap) {
  $mode = $cap.codex.mode
  if ($mode -eq "unsupported") { return "unsupported" }
  if ($mode -eq "plugin") {
    if ($cap.codex.id -eq "superpowers@openai-curated" -and $codexSuperpowers) { return "ok" }
    if (Test-AnyPortableSkills @($cap.portable_skills)) { return "ok-skill-fallback" }
    return "missing-plugin:$($cap.codex.id)"
  }
  if ($mode -eq "skill") {
    if ($cap.codex.path -and (Test-SkillPath $cap.codex.path)) { return "ok" }
    if (Test-AnyPortableSkills @($cap.portable_skills)) { return "ok" }
    return "missing-skill"
  }
  return "unknown-mode:$mode"
}

Write-Host "Harness verify — repo: $RepoRoot"
Write-Host ("Skills dir: {0} ({1} skills)" -f $SkillsRoot, (Get-ChildItem $SkillsRoot -Directory -ErrorAction SilentlyContinue).Count)
Write-Host ""

foreach ($cap in $inventory.capabilities) {
  $results = @{}
  if ($Harness -in @("all", "claude")) { $results.claude = Check-Claude $cap }
  if ($Harness -in @("all", "grok")) { $results.grok = Check-Grok $cap }
  if ($Harness -in @("all", "codex")) { $results.codex = Check-Codex $cap }

  foreach ($h in $results.Keys) {
    $status = $results[$h]
    $ok = $status -eq "ok" -or $status -eq "ok-skill-fallback" -or $status -eq "unsupported"
    if (-not $ok) {
      $failures.Add("$($cap.id)/${h}: $status")
    }
    $rows.Add([pscustomobject]@{
      id     = $cap.id
      harness = $h
      status = $status
    })
  }
}

$rows | Format-Table -AutoSize | Out-String | Write-Host

# Portable skill presence for all non-empty lists
$skillGaps = [System.Collections.Generic.List[string]]::new()
foreach ($cap in $inventory.capabilities) {
  foreach ($s in @($cap.portable_skills)) {
    if (-not (Test-SkillPath $s)) { $skillGaps.Add("$($cap.id) -> $s") }
  }
}
if ($skillGaps.Count -gt 0) {
  Write-Host "Missing portable skills:" -ForegroundColor Yellow
  $skillGaps | ForEach-Object { Write-Host "  $_"; $failures.Add("skill:$_") }
}

if ($failures.Count -gt 0) {
  Write-Host "`nFAILED ($($failures.Count)):" -ForegroundColor Red
  $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
  exit 1
}

Write-Host "`nPASS — all declared inventory rows are present or explicitly unsupported for harness=$Harness" -ForegroundColor Green
exit 0
