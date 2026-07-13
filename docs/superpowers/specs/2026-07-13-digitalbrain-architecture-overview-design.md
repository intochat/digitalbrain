# DigitalBrain Architecture Overview — Visual Design

Date: 2026-07-13

Status: approved

## Purpose

Provide the owner with a standalone, interactive explanation of the approved DigitalBrain v3 target architecture and its most important runtime flows. The visual is an orientation tool, not a second architecture specification.

## Chosen approach

Use a layered interactive view with three modes:

1. System map: five architecture layers with selectable end-to-end flows.
2. Runtime topology: processes, replicas, durable grains, and storage resources.
3. Keep and remove: the retained correctness rails and the deletion targets.

The system map supports five traces: programming and installing a Feature, handling a Gmail event, proposing and verifying a Salesforce effect, updating or rolling back a Feature, and Memory remember/recall.

Selecting a component shows its responsibility, trust boundary, principal dependencies, and restart behavior. The first render remains useful without interaction, while keyboard-accessible controls progressively focus the diagram.

## Visual hierarchy

The default system map presents:

1. Flutter and MCP/UI Edge.
2. Feature source, FeatureBuilder, releases, and FeatureHost.
3. RuntimeHost, FeatureHubGrain, FeatureInstallationGrain, capability dispatcher, and the retained effect authority.
4. Google and Salesforce Integration Contracts plus Runtime/Hosting.
5. Azurite-backed Orleans state, Feature artifacts, and lexical Memory.

Color is secondary to text and shape. Focus is expressed through selection and opacity, so the architecture remains readable in light mode, dark mode, and without color discrimination.

## Artifact boundaries

- Editable visualization fragment: the thread visualization workspace.
- Standalone rendered document: `docs/architecture/digitalbrain-architecture-overview.html`.
- Source of architectural truth: `docs/superpowers/specs/2026-07-13-digitalbrain-programmable-features-design-v3.md`.
- Implementation handoff: `docs/prompts/2026-07-13-digitalbrain-system-implementation-prompt.md`.

The visualization contains no remote data dependency, no API calls, and no code comments.

## Verification

- Render the standalone document with the visualization renderer.
- Inspect it at desktop and narrow widths.
- Exercise every mode, flow, and component-selection control.
- Check the browser console for errors.
- Scan generated and authored artifacts for placeholders, comments, stale Behavior terminology, and broken references.
- Confirm only intended documentation artifacts are staged.
