---
name: onboarding-buddy
description: "Use this skill at the beginning of any session or task to onboard and synchronize local state with live GitHub Project 8 statuses and open issues. It contains guidelines for authenticating and executing GitHub CLI commands, running the active task synchronizer script, initializing planning files, and defining custom subagents for code generation and testing."
---

# Onboarding Buddy Skill

Use this skill at the start of every pair-programming session or when picking up a new Epic/issue to align local workspaces with the repository's active roadmaps and GitHub Project 8 issues.

---

## 1. The Core Onboarding Workflow

When picking up a new task or starting a session:

1. **Verify GitHub Authentication & Fetch Status**:
   Always run the dynamic sync python script first:
   ```pwsh
   python .roadmap_scratch\sync_onboarding.py
   ```
   This script queries `gh` CLI for issues assigned to you and any active `In Progress` task inside Project 8, and details current uncommitted git changes.

2. **Align with the Roadmap Spine**:
   Read the canonical roadmap spine in `docs/v3/VISION.md` §11 and the guidelines inside `CLAUDE.md` to align on conventions and requirements.

3. **Transition to Planning Mode**:
   Create or update [implementation_plan.md](file:///C:/Users/vhorb/.gemini/antigravity/brain/ff44bcb5-31f8-4e47-b7e3-4092290c6d53/implementation_plan.md) with detailed technical architecture and verification plans. Break down items into [task.md](file:///C:/Users/vhorb/.gemini/antigravity/brain/ff44bcb5-31f8-4e47-b7e3-4092290c6d53/task.md) checkboxes and obtain user approval before writing C# / InoLang.

---

## 2. GitHub CLI (GH) Usage Best Practices

In this workspace, the local `GITHUB_TOKEN` environment variable might contain an invalid/expired token, while the system keyring holds a valid active token for LeftTwixWand.

- > [!IMPORTANT]
  > **Always bypass environment tokens**: Prepend `$env:GITHUB_TOKEN=$null;` (PowerShell) or clear `GITHUB_TOKEN` before running `gh` commands so that it falls back to the secure keyring.
  >
  > *Example:*
  > ```pwsh
  > $env:GITHUB_TOKEN=$null; gh issue list --assignee @me
  > ```

- **Querying Project 8 directly**:
  Use `gh project` commands to list or modify task statuses:
  ```pwsh
  # List all items in Project 8
  $env:GITHUB_TOKEN=$null; gh project item-list 8 --owner LeftTwixWand --format json
  ```

---

## 3. Defining Custom Subagents for Execution

To execute tasks faster, you can define specialized subagents using the `define_subagent` tool:

### **Spec-First Invariant Coder (`spec-first-coder`)**
- **System Prompt**: Focuses on implementing neurons and InoLang states, adhering strictly to the spec-first rule. Must write projection tests (`*ProjectionTests.cs`) first, make sure they are red, and then write C# / InoLang logic to make them green.
- **Enable Write Tools**: `true`

### **Verification and Test Engine (`tester-agent`)**
- **System Prompt**: Focused purely on running tests, compiling intermediate assets, analyzing test failures, and ensuring that flakiness in Orleans cluster tests is detected and cataloged correctly.
- **Enable Write Tools**: `true`

---

## 4. Operational Invariant Reminders

- **Spec-First Invariant**: Never write a neuron or store behavior without a matching scenario projection test.
- **Aspire composition**: Always boot the silo using `aspire start` rather than `dotnet run`. Use `mcp__aspire__execute_resource_command` with the `rebuild` parameter to reload changes on a running Silo.
- **No Domain Sprawl**: Keep domain-specific storage, settings, and helper grains inside the respective domain folders.
