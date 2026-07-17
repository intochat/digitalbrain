# DigitalBrain VitePress Documentation Site Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an ino-style VitePress documentation website that starts as the `brain-docs` resource whenever the DigitalBrain Aspire AppHost runs.

**Architecture:** A standalone `website/` npm project owns the documentation, custom VitePress theme, and homepage. The existing C# AppHost registers it with the first-party `Aspire.Hosting.JavaScript` integration through `AddViteApp`, which handles npm installation, the Vite development process, and the Aspire-managed HTTP endpoint.

**Tech Stack:** VitePress 1.6.4, Vue 3, Node.js test runner, Aspire 13.4 JavaScript hosting integration, C# AppHost.

## Global Constraints

- Match the structure and dark neural visual language of `sources/Projects/ino/website`.
- Keep documentation truthful by distinguishing current repository state from intended architecture.
- Use an Aspire-managed port and expose the documentation URL in the dashboard.
- Do not add a second web server or hard-coded documentation port.
- Keep `sources/` untouched.
- Add no source-code comments.

---

### Task 1: Executable documentation contract

**Files:**
- Create: `website/tests/site.test.mjs`
- Create: `website/package.json`

**Interfaces:**
- Consumes: Node.js built-in `node:test`, repository files.
- Produces: `npm test` contract covering required pages, homepage copy, npm scripts, and Aspire registration.

- [ ] **Step 1: Write the failing site contract**

Create a Node test that asserts:

```javascript
const requiredPages = [
  'index.md',
  'guide/index.md',
  'guide/architecture.md',
  'guide/neurons.md',
  'guide/synapses.md',
  'guide/modules.md',
  'guide/programming-model.md',
  'guide/webhooks.md',
  'contributing/index.md',
  'reference/status.md'
]
```

It must also assert that `package.json` exposes `dev`, `build`, `preview`, and `test`; the homepage contains “Everything addressable is a neuron”; and `hosts/DigitalBrain.AppHost/AppHost.cs` contains `AddViteApp("brain-docs", "../../website")`.

- [ ] **Step 2: Verify the contract fails**

Run: `node --test website/tests/site.test.mjs`

Expected: FAIL because the VitePress files and Aspire registration do not exist.

- [ ] **Step 3: Add the npm project**

Create `website/package.json` with:

```json
{
  "name": "digitalbrain-docs",
  "private": true,
  "type": "module",
  "scripts": {
    "dev": "vitepress dev --host 0.0.0.0",
    "build": "vitepress build",
    "preview": "vitepress preview --host 0.0.0.0",
    "test": "node --test tests/*.test.mjs"
  },
  "devDependencies": {
    "vitepress": "^1.6.4"
  }
}
```

---

### Task 2: Ino-style VitePress website

**Files:**
- Create: `website/.vitepress/config.mts`
- Create: `website/.vitepress/theme/index.ts`
- Create: `website/.vitepress/theme/custom.css`
- Create: `website/.vitepress/theme/components/HomePage.vue`
- Create: `website/.vitepress/theme/components/NeuronGraph.vue`
- Create: `website/public/logo.svg`
- Create: `website/index.md`
- Create: `website/guide/index.md`
- Create: `website/guide/architecture.md`
- Create: `website/guide/neurons.md`
- Create: `website/guide/synapses.md`
- Create: `website/guide/modules.md`
- Create: `website/guide/programming-model.md`
- Create: `website/guide/webhooks.md`
- Create: `website/contributing/index.md`
- Create: `website/reference/status.md`
- Create: `website/reference/decisions.md`

**Interfaces:**
- Consumes: VitePress default theme and Vue component registration.
- Produces: a responsive homepage, local search, Guide/Contributing/Reference navigation, and initial documentation.

- [ ] **Step 1: Add site configuration and theme registration**

Configure the title `DigitalBrain`, local search, GitHub link, guide/reference sidebars, custom CSS, `HomePage`, and `NeuronGraph`.

- [ ] **Step 2: Add the homepage**

Build a full-width dark homepage with:

- “DigitalBrain” hero.
- “An operating system built from neurons and synapses.”
- Animated neural mesh.
- Neuron and Synapse primitive cards.
- Kernel, module, connector, and UI layers.
- Links into the Guide and Architecture pages.

- [ ] **Step 3: Add the initial documentation**

Document the project overview, architecture, neuron and synapse definitions, module anatomy, typed programming model, webhook ingress, contribution workflow, implementation status, and unresolved decisions.

- [ ] **Step 4: Install and build**

Run: `npm install --no-audit --no-fund`

Run: `npm run build`

Expected: VitePress exits with code 0 and writes `website/.vitepress/dist/index.html`.

---

### Task 3: Aspire integration

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
- Modify: `hosts/DigitalBrain.AppHost/AppHost.cs`

**Interfaces:**
- Consumes: `Aspire.Hosting.JavaScript` and `website/package.json`.
- Produces: an externally visible `brain-docs` Vite resource with an Aspire-managed HTTP endpoint.

- [ ] **Step 1: Add the hosting package**

Add central package version:

```xml
<PackageVersion Include="Aspire.Hosting.JavaScript" Version="13.4.6" />
```

Add the AppHost package reference:

```xml
<PackageReference Include="Aspire.Hosting.JavaScript" />
```

- [ ] **Step 2: Register the documentation resource**

Add before `builder.Build().Run()`:

```csharp
builder.AddViteApp("brain-docs", "../../website")
    .WithExternalHttpEndpoints();
```

- [ ] **Step 3: Verify the contract turns green**

Run: `npm test`

Expected: all site contract tests pass.

- [ ] **Step 4: Verify AppHost compilation**

Run: `dotnet build hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`

Expected: build succeeds with zero errors.

- [ ] **Step 5: Verify live Aspire topology**

Restart the AppHost, then confirm `brain-docs` is running and has an HTTP URL in the Aspire dashboard.

- [ ] **Step 6: Verify the rendered site**

Open the Aspire-provided `brain-docs` URL and inspect desktop and narrow layouts. Confirm homepage navigation, local documentation links, neural animation, and absence of browser console errors.

- [ ] **Step 7: Run repository gates**

Run: `dotnet test --logger "console;verbosity=minimal"`

Run: `npm run build` from `website/`.

Expected: all tests pass and the documentation production build succeeds.
