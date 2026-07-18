# Claude Code Session — TripRadar Issue Creation

Copy-paste the prompt below into a new Claude Code session opened in the TripRadar repository.

**Prerequisites:** IAW issues must be created first (see `session-iaw.md`). You'll need the actual IAW issue numbers to set up cross-repo blocker references.

---

## Prompt

```
I need you to create GitHub issues, a milestone, labels, and a GitHub project in this repository (RoseXTechnology/TripRadar) for the Companion AI v1 initiative.

Read the full spec at `docs/companion-ai/spec.md`, issue details at `docs/companion-ai/tripradar-issues.md`, and dependency map at `docs/companion-ai/dependencies.md`.

## Step 1: Check existing state

Run these commands to understand what exists:
- `gh issue list --state all --limit 50`
- `gh milestone list --state all`
- `gh label list --limit 50`
- `gh project list`

## Step 2: Create milestone

- **Name:** `Companion v1`
- **Description:** "IAW-powered AI travel companion — proactive, preference-aware, vision-capable"

## Step 3: Create labels

Create these labels:
- `companion ai` — Color: `#7C3AED` — "AI companion features powered by IAW"
- `area: agents` — Color: `#F59E0B` — "TripRadar.Agents project — travel domain agents"

## Step 4: Create GitHub Project

Create a GitHub project:
- **Name:** `Companion AI`
- **Description:** "All work related to IAW integration and AI travel companion"

Use `gh project create --title "Companion AI" --body "All work related to IAW integration and AI travel companion"`

## Step 5: Create issues

Read `docs/companion-ai/tripradar-issues.md` for the full issue descriptions. Create all 23 issues (TR-01 through TR-23) using `gh issue create`.

For each issue:
- Set `--milestone "Companion v1"`
- Set appropriate `--label` values as specified in the file
- Include blocker references in the issue body where specified (format: "Blocked by InteractiveAgents/IAW#XX")

IMPORTANT: Before creating any issues, present me the full list with titles, labels, and blocker references for my approval. Only create after I approve.

The IAW issue numbers for cross-repo references (ALREADY CREATED):
- IAW-01 (NuGet publishing) = InteractiveAgents/IAW#45
- IAW-05 (Vision tier) = InteractiveAgents/IAW#49
- IAW-06 (Kafka bridge) = InteractiveAgents/IAW#50
- IAW-08 (Proactive patterns) = InteractiveAgents/IAW#52
- IAW-09 (Token budgets) = InteractiveAgents/IAW#53

> **NOTE:** All issues have already been created. This file is kept for reference only.

Here are the issues to create:

### Phase 2 — Foundation
1. **[Agents] Add IAW NuGet packages to solution** — `companion ai`, `area: agents` — Blocked by IAW NuGet publishing
2. **[Agents] Create TripRadar.Agents project scaffold** — `companion ai`, `area: agents` — Blocked by #1
3. **[Agents] Create TripRadar.Agents.Host Orleans silo** — `companion ai`, `area: agents` — Blocked by #2
4. **[Infra] Wire IAW into Aspire AppHost** — `companion ai`, `area: agents` — Blocked by #3
5. **[Bot] Connect Bot as Orleans client** — `companion ai`, `area: telegram bot` — Blocked by #4
6. **[Infra] Add Qdrant vector database to Aspire** — `companion ai`, `area: agents`

### Phase 3 — Core Agents
7. **[Agents] Implement UserPreferenceAgent** — `companion ai`, `area: agents` — Blocked by #4
8. **[Agents] Implement FlightSearchAgent** — `companion ai`, `area: agents` — Blocked by #4, #7
9. **[Agents] Implement HotelSearchAgent** — `companion ai`, `area: agents` — Blocked by #4, #7
10. **[Agents] Implement PlaceDiscoveryAgent** — `companion ai`, `area: agents` — Blocked by #4, #7
11. **[Bot] Build Telegram conversational interface for agents** — `companion ai`, `area: telegram bot` — Blocked by #5, #8
12. **[MiniApp] Build Companion chat page** — `companion ai`, `area: miniapp` — Blocked by #5, #8

### Phase 4 — Vision & Budget Agents
13. **[Agents] Implement CurrencyAgent with vision** — `companion ai`, `area: agents` — Blocked by #4, IAW vision tier
14. **[Agents] Implement BudgetAgent with receipt scanning** — `companion ai`, `area: agents` — Blocked by #4, #13, IAW vision tier
15. **[Agents] Implement WeatherMonitorAgent** — `companion ai`, `area: agents` — Blocked by #4, IAW proactive patterns
16. **[Agents] Implement PriceWatchAgent with Kafka bridge** — `companion ai`, `area: agents` — Blocked by #4, IAW Kafka bridge
17. **[Agents] Implement TripTimelineAgent** — `companion ai`, `area: agents` — Blocked by #4, IAW proactive patterns
18. **[MiniApp] Wire Money features to BudgetAgent and CurrencyAgent** — `companion ai`, `area: miniapp` — Blocked by #13, #14

### Phase 5 — Deep Intelligence
19. **[Agents] Implement TripContextAgent** — `companion ai`, `area: agents` — Blocked by #4
20. **[Agents] Implement EventDiscoveryAgent** — `companion ai`, `area: agents` — Blocked by #4, #7
21. **[Agents] Implement ItineraryPlannerAgent** — `companion ai`, `area: agents` — Blocked by #10, #20, #7
22. **[Agents] Implement proactive recommendation engine** — `companion ai`, `area: agents` — Blocked by #7, #10, #19
23. **[Agents] Per-user token accounting for Companion AI** — `companion ai`, `area: agents`, `area: server` — Blocked by #4, IAW token budgets

## Step 6: Add issues to project

After creating all issues, add them to the "Companion AI" project:
```bash
# Get project number
gh project list

# For each issue, add to project
gh project item-add PROJECT_NUMBER --owner RoseXTechnology --url ISSUE_URL
```

## Step 7: Report

List all created issues with their numbers, titles, labels, milestone, and blocker references. Format as a table.
```
