# DigitalBrain User Flows (post-SIM)

OS4 continuation: ino now has live persona composition (installed bundles from ListInstalled in enrich/persona; hardcoded static narrative deleted) + full OS tools (list_installed_experiences, install/uninstall_experience, pin/move_widget, run_experience, describe_workspace) as emits-as-tools (direct PinSurface etc) or proposal for destructive. Orientation exchange ("what's on my machine") and persona-changes-on-install covered by fresh grain reads on every AgentRequest. Gate held per plan. See OS-FROM-INO-PLAN.md.

OS3 continuation note: uninstall + requires (pre-install check surfaces actionable buttons for missing) + Installed section (with Uninstall) + N-1 (journal preserved, counts shrink) added; capsule defaults (region/pinned/order from os/*.ino headers) applied at install. See docs/OS-FROM-INO-PLAN.md §3.7 + handoff. All rituals (sim after delta, aspire, Context7) followed; core gate 20p/1f.

## Creator Run -> simulation (SIM4)
In Flutter Creator editor:
1. Author .ino with rule/scenario block (or triggers).
2. ValidateIno (surface errors if bad).
3. Pack (produces .brain capsule with manifest + inoContent + HasRules).
4. Run (sends RunSimulation(ino:<id>) via brain).
5. Report surface appears (Card: per-scenario green/red rows + Re-run / Open artifacts buttons; WidgetTree.Render contains the scenario name).
L2 unification: same quarantine/replay/emit path as evidence install.
TUI: /simulate <filter> (e.g. /simulate ino:standup or tag:Rules).
ino agent: run_simulation(filter) tool (journaled).
aspire resource (kernel): run-simulation typed command (next to publish-experience).

(Other flows in VISION.md / docs.)