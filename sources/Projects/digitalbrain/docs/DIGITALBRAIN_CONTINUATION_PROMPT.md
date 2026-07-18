# DigitalBrain Continuation Prompt

Use this prompt to continue the cleanup in a new Codex session.

```text
You are working in E:\digitalbrain on the DigitalBrain repo.

Goal:
Clean up the current project toward the architecture described in:
- docs/DIGITALBRAIN_CLEANUP_ACTION_PLAN.md
- docs/DIGITALBRAIN_TARGET_TREE.md

Current desired direction:
- DigitalBrain.slnx is the canonical solution.
- DigitalBrain is a new operating system built from neurons and synapses.
- A synapse is the single message/data contract. Broadcasts are synapses with broadcast routing, not Signals.
- Experiences live in .ino files. A .ino file is like a feature file plus runtime behavior: contracts, behavior, UI, scenarios, and marketplace metadata.
- Ino can receive a prompt, generate a new .ino, compile/simulate it, persist it, hot-register it, and stream authoring progress to a local console/RFW surface.
- Marketplace should support private implementation silos with public contracts. A consumer can install public contracts and simulate/call private neurons without seeing implementation.
- All tests should migrate to a central BDD simulation harness. Reqnroll/Gherkin is allowed for simulation features, but not as per-neuron .feature + .Steps.cs triplets.
- The app should normally stay running through Aspire. Rebuild/restart the affected resource when source changes. Do not depend on dynamic mutation of the Aspire AppHost graph after Build().RunAsync(); use isolated simulation AppHosts or AppHost restart for topology changes.

Important current repo facts:
- The working tree may already be dirty. Do not revert user changes.
- sdk/DigitalBrain.SDK appears to have a large in-progress move from old flat folders to provider/domain folders such as sdk/DigitalBrain.SDK/DigitalBrain and sdk/DigitalBrain.SDK/Microsoft. Treat that as existing user work.
- DigitalBrain.slnx already includes AppHost, Runtime, Kernel, InoLang, InoLang.Tests, SDK, and Flutter.
- InoCompiler, Interpreter, and ScenarioRunner already exist under inolang/DigitalBrain.InoLang.
- InoCreatorNeuron and InoAuthoringLoop already implement prompt -> .ino -> compile -> simulate -> persist -> hot-register.
- DurableTaskCompletionSourceGrain already exists.
- MarketplaceNeuron, LicenseNeuron, LocalBundleInstaller, and marketplace tests already exist.
- DigitalBrain.Kernel still references Gherkin and embeds .feature files.
- InoToCSharpTranspiler still emits Reqnroll .feature/.Steps.cs artifacts.
- Legacy Signal vocabulary still exists in SignalAttribute, [Signal] attributes, comments, manifests, and tests.
- CLAUDE.md and README.md are stale and reference missing docs/v3 and docs/v4 folders.

First implementation slice:
1. Read docs/DIGITALBRAIN_CLEANUP_ACTION_PLAN.md and docs/DIGITALBRAIN_TARGET_TREE.md.
2. Run git status --short.
3. Update README.md and CLAUDE.md to point to the new canonical docs and remove stale v3/v4/triplet/project references.
4. Do not delete old docs yet unless the user explicitly asks. Produce a deletion list first.
5. Verify with rg that README.md and CLAUDE.md no longer reference missing docs/v3/docs/v4 or old projects as current architecture.
6. Run dotnet build DigitalBrain.slnx --no-restore if restore state allows it. If sandbox/network blocks required work, ask for approval through the command runner.

Guardrails:
- Do not use git reset or checkout to revert changes.
- Do not delete runtime/generated folders until you have confirmed they are untracked or intentionally obsolete.
- Use apply_patch for manual file edits.
- Keep changes scoped to the current slice.
- If touching Aspire behavior, use the Aspire skill and prefer aspire start/resource commands over dotnet run on AppHost.
- If touching Reqnroll/Gherkin, consult the official Reqnroll Gherkin reference.
- If touching Aspire resource commands, consult the official Aspire custom resource command docs.

Useful commands:
git status --short
rg "docs/v3|docs/v4|DigitalBrain.Core|ServiceDefaults|NeuronTesting|Reqnroll|\\.feature|Signal" README.md CLAUDE.md docs
rg --files -g "*.feature" -g "*.Steps.cs"
dotnet build DigitalBrain.slnx --no-restore
dotnet test DigitalBrain.slnx --no-build --max-parallel-test-modules 1
```

## Recommended Next Slices

1. Docs truth pass:
   update `README.md` and `CLAUDE.md`.
2. Signal vocabulary inventory:
   list every live `Signal`, `signal`, `HandledSignalSubscriptions`, and
   `ContractKind.Signal` reference; classify as compatibility shim, test, or
   migration target.
3. Simulation harness scaffold:
   add `simulations/DigitalBrain.Simulations` with generic Reqnroll steps.
4. Marketplace contract split:
   add public contract manifest generation before adding a new contracts
   project.
5. Aspire operation commands:
   add or standardize resource commands for rebuild/restart/reload/simulate.

## Source Links

- Reqnroll Gherkin reference:
  https://docs.reqnroll.net/latest/gherkin/gherkin-reference.html
- Aspire custom resource commands:
  https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/custom-resource-commands

