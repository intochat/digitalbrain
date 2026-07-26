import assert from 'node:assert/strict'
import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'

const testDirectory = dirname(fileURLToPath(import.meta.url))
const repositoryRoot = resolve(testDirectory, '..', '..')
const docsRoot = join(repositoryRoot, 'docs')

const read = (...segments) => readFileSync(join(repositoryRoot, ...segments), 'utf8')

const contentPages = [
  'index.md', 'concepts.md', 'architecture.md', 'quickstart.md', 'contributing.md',
  'packages.md', 'specification.md',
]

test('documentation project exposes the standard VitePress commands', () => {
  const packageJson = JSON.parse(read('docs', 'package.json'))

  assert.equal(packageJson.private, true)
  assert.equal(packageJson.type, 'module')
  assert.match(packageJson.scripts.dev, /vitepress dev/)
  assert.equal(packageJson.scripts.build, 'vitepress build')
  assert.match(packageJson.scripts.preview, /vitepress preview/)
  assert.equal(packageJson.scripts.test, 'node --test tests/*.test.mjs')
  assert.equal(packageJson.devDependencies.vitepress, '^1.6.4')
})

test('the specification is regenerated before the site is tested or built', () => {
  const packageJson = JSON.parse(read('docs', 'package.json'))

  assert.equal(packageJson.scripts.pretest, 'node tools/render-specification.mjs')
  assert.equal(packageJson.scripts.prebuild, 'node tools/render-specification.mjs')
  assert.equal(existsSync(join(docsRoot, 'tools', 'render-specification.mjs')), true)
})

test('every documented page exists and nothing else claims to be documentation', () => {
  for (const page of contentPages) {
    assert.equal(existsSync(join(docsRoot, page)), true, `${page} must exist`)
  }

  const retiredSections = ['guide', 'build', 'getting-started', 'contributing', 'reference', 'packages', 'status.md']
  for (const section of retiredSections) {
    assert.equal(existsSync(join(docsRoot, section)), false, `${section}/ must stay deleted`)
  }
  // the interactive architecture diagram lives in the site's one custom theme
  assert.equal(existsSync(join(docsRoot, '.vitepress', 'theme', 'index.js')), true)
  assert.equal(existsSync(join(docsRoot, '.vitepress', 'theme', 'ArchitectureMap.vue')), true)
  const themeIndex = read('docs', '.vitepress', 'theme', 'index.js')
  assert.match(themeIndex, /extends:\s*DefaultTheme/)
  assert.match(themeIndex, /app\.component\('ArchitectureMap'/)
  assert.match(read('docs', 'architecture.md'), /<ArchitectureMap\s*\/>/)
})

test('navigation and sidebar reach every page', () => {
  const config = read('docs', '.vitepress', 'config.mts')

  assert.match(config, /title: 'DigitalBrain'/)
  assert.match(config, /provider: 'local'/)
  assert.match(config, /github\.com\/intochat\/digitalbrain/)
  assert.doesNotMatch(config, /InteractiveAgents/)

  for (const link of ['/quickstart', '/concepts', '/architecture', '/specification', '/packages', '/contributing']) {
    assert.ok(config.includes(`'${link}'`), `config must link ${link}`)
  }
})

test('the site is configured for the digitalbrain.tech GitHub Pages apex', () => {
  const config = read('docs', '.vitepress', 'config.mts')
  const cname = read('docs', 'public', 'CNAME').trim()
  const pagesWorkflow = read('.github', 'workflows', 'docs-pages.yml')

  assert.match(config, /base:\s*['"]\/['"]/)
  assert.match(config, /hostname:\s*['"]https:\/\/digitalbrain\.tech['"]/)
  assert.equal(cname, 'digitalbrain.tech')
  assert.match(pagesWorkflow, /npm test/)
  assert.match(pagesWorkflow, /npm run build/)
  assert.match(pagesWorkflow, /actions\/upload-pages-artifact@v5/)
  assert.match(pagesWorkflow, /actions\/deploy-pages@v5/)
  assert.match(pagesWorkflow, /path:\s*docs\/\.vitepress\/dist/)
  assert.match(pagesWorkflow, /cancel-in-progress:\s*false/)
  assert.match(pagesWorkflow, /branches:\s*\[master\]/)
})

test('CI keeps framework on every master event and docs only on pull requests', () => {
  const ci = read('.github', 'workflows', 'ci.yml')
  const pages = read('.github', 'workflows', 'docs-pages.yml')
  const dependabot = read('.github', 'dependabot.yml')

  assert.match(ci, /^ {2}framework:/m)
  assert.match(ci, /dotnet test DigitalBrain\.slnx -c Release/)
  assert.match(ci, /if:\s*github\.event_name\s*==\s*'pull_request'/)
  assert.match(ci, /npm test/)
  assert.match(ci, /npm run build/)
  assert.match(ci, /cache-dependency-path:\s*docs\/package-lock\.json/)
  assert.doesNotMatch(ci, /deploy-pages/)
  assert.doesNotMatch(ci, /nuget\.org|dotnet nuget push|dotnet pack/i)

  assert.match(pages, /npm ci --no-audit --no-fund/)
  assert.match(pages, /npm test/)
  assert.match(pages, /npm run build/)
  assert.doesNotMatch(pages, /dotnet test/)

  assert.match(dependabot, /package-ecosystem:\s*github-actions/)
  assert.match(dependabot, /package-ecosystem:\s*npm/)
  assert.match(dependabot, /directory:\s*\/docs/)
  assert.match(dependabot, /package-ecosystem:\s*nuget/)
})

test('the homepage tells the neurons, synapses, and executable tests story', () => {
  const homepage = read('docs', 'index.md')

  assert.match(homepage, /layout: home/)
  assert.match(homepage, /Neurons/)
  assert.match(homepage, /Synapses/)
  assert.match(homepage, /TestBrain/)
  assert.match(homepage, /Orleans/)
  assert.match(homepage, /Aspire/)
})

test('concepts define the three primitives and the whole vocabulary', () => {
  const concepts = read('docs', 'concepts.md')

  assert.match(concepts, /IHandle<TSynapse>/)
  assert.match(concepts, /IEmit<TSynapse>/)
  assert.match(concepts, /journaled grain/)
  assert.match(concepts, /correlation and causation lineage/)
  assert.match(concepts, /method-scoped development testing primitive/)

  const glossaryTerms = [
    'Neuron', 'Synapse', 'Capability request', 'Module', 'Behavior', 'Registry',
    'LLM', 'Agent', 'Orchestration', 'Group Chat', 'Participant', 'Executor', 'Capability',
    'Task', 'Goal', 'Attempt', 'Worker', 'Workflow', 'Blocker', 'Result', 'Successor Task',
    'Countdown', 'Reminder', 'Interval schedule', 'Calendar schedule', 'Occurrence',
  ]
  for (const term of glossaryTerms) {
    assert.ok(concepts.includes(`**${term}**`), `concepts must define ${term}`)
  }

  const avoidLines = concepts.match(/^_Avoid_: /gm) ?? []
  assert.ok(avoidLines.length >= 26, `every glossary term needs an _Avoid_ line, found ${avoidLines.length}`)
})

test('the architecture page is module-organized and states each status once', () => {
  const architecture = read('docs', 'architecture.md')

  for (const heading of [
    'The vision', 'The kernel', 'The module model', 'The modules',
    'Behaviors and scripting', 'Registry and discovery', 'Hosting and durability',
    'Testing',
  ]) {
    assert.ok(architecture.includes(heading), `architecture must have a ${heading} section`)
  }

  assert.match(architecture, /real three-silo DigitalBrainFixture/)
  assert.match(architecture, /assembly-owned DigitalBrainAppHostFixture<TAppHost>/)
  assert.match(architecture, /method-scoped RunningAppHost/)
  assert.match(architecture, /host\.Resource\("silo"\)/)
  assert.match(architecture, /never enumerates or kills processes by name/)
  assert.match(architecture, /ConfigureChatClient/)
  assert.match(architecture, /`BehaviorNeuron` is the Neuron and its single-file program is not/)
  assert.match(architecture, /IDigitalBrainNeuron|DigitalBrainNeuron/)
  assert.match(architecture, /IBehavior/)

  const retiredHostedTesting = new RegExp([
    'Hosted' + 'Application',
    'Hosted' + 'Scen' + 'ario',
    'DefaultTracked' + 'ProcessNames',
    'GetProcesses' + 'ByName',
    'IsExclusive' + 'Held',
    'Exclusive' + 'Owner',
  ].join('|'))
  assert.doesNotMatch(architecture, retiredHostedTesting)
  assert.doesNotMatch(read('docs', 'packages.md'), retiredHostedTesting)

  for (const module of ['AI', 'Tasks', 'Google', 'Salesforce', 'Time', 'Flutter', 'Memory']) {
    assert.ok(
      new RegExp(`### .*\\b${module}\\b`).test(architecture),
      `architecture must have a ${module} module section`)
  }

  const built = architecture.match(/^Status: Built$/gm) ?? []
  const builtCountdown = architecture.match(/^Status: Built — Countdown only$/gm) ?? []
  const builtFlutter = architecture.match(
    /^Status: Built \(first-vertical vocabulary \+ L0\/L1 journal proofs \+ C# northbound UI edge \+ module-owned `Flutter\.Aspire\.Hosting` WithUiEdge\/WithFlutterHost projection \+ \*\*pure-Dart\*\* headless host at `clients\/digitalbrain_flutter` \+ Windows chrome in nested `clients\/digitalbrain_flutter\/shell\/` \(`shell\/lib\/main\.dart` \+ `shell\/windows\/`\) — \*\*code and L0\/L1 only\*\*\); Designed \([^)]+\); \*\*residual unproven:\*\*.*\*\*not\*\* Built-live$/gm) ?? []
  const designed = architecture.match(/^Status: Designed$/gm) ?? []
  assert.equal(built.length, 4, 'AI, Tasks, Google, and Salesforce are built')
  assert.equal(builtCountdown.length, 1, 'Time is built — Countdown only')
  assert.equal(builtFlutter.length, 1, 'Flutter pure-Dart + nested shell/ chrome Built at code/L0/L1; residual not Built-live; IdP/observation Designed')
  assert.equal(designed.length, 1, 'Behaviors section is designed')
  assert.match(architecture, /Designed \(full product chrome beyond key\/title shell, product journal observation on IDigitalBrain, multi-principal IdP edge\)/)
  assert.match(architecture, /## 5\. Behaviors and scripting\r?\n\r?\nStatus: Designed/)
  assert.doesNotMatch(architecture, /Windows widget chrome polish/)
  assert.doesNotMatch(architecture, /Windows chrome via `lib\/main\.dart`\/`windows\/`/)
  assert.doesNotMatch(read('docs', 'packages.md'), /Windows Flutter chrome Designed|Designed \(Windows widget chrome polish\)/)

  assert.match(architecture, /WithFlutterHost\(\)` = Desktop/)
  assert.match(architecture, /WithFlutterHost<DesktopHost>\(\)/)
  assert.match(architecture, /WithFlutterHost<HeadlessHost>\(\)/)
  assert.match(architecture, /WithFlutterHost\(\)` \/ `<DesktopHost>`/)
  assert.match(architecture, /\*\*No Auto\.\*\*|\*\*no Auto\*\*|no silent Auto fallback/)
  assert.doesNotMatch(architecture, /honest Auto\//)
  assert.match(
    read('docs', 'packages.md'),
    /WithFlutterHost\(\)[\s\S]{0,120}?Desktop|Desktop[\s\S]{0,80}?WithFlutterHost\(\)/)
  assert.match(read('docs', 'packages.md'), /HeadlessHost/)
  assert.match(read('docs', 'packages.md'), /not\*\* Built-live|not Built-live|as Built-live/)
  assert.doesNotMatch(read('docs', 'packages.md'), /\bAuto host\b|WithFlutterHost\s*<\s*Auto/)

  const productAppHost = read('hosts', 'DigitalBrain.AppHost', 'AppHost.cs')
  assert.match(productAppHost, /\.WithFlutterHost\(\)/)
  assert.doesNotMatch(productAppHost, /WithFlutterHost\s*<\s*HeadlessHost\s*>/)
  assert.doesNotMatch(productAppHost, /WithFlutterHost\s*<\s*Auto/)

  assert.match(architecture, /human-approved proposal/)
  assert.match(architecture, /Runtime behavior installation is designed and not yet built/)
  assert.doesNotMatch(architecture, /REFINED-ARCHITECTURE|APPROVED-ARCHITECTURE/)
})

test('the quickstart matches the sample that CI actually runs', () => {
  const quickstart = read('docs', 'quickstart.md')
  const contract = read('samples', 'DigitalBrain.Quickstart.Contracts', 'IGreeter.cs')
  const synapses = read('samples', 'DigitalBrain.Quickstart.Contracts', 'GreetingSynapses.cs')
  const module = read('samples', 'DigitalBrain.Quickstart', 'QuickstartModule.cs')
  const host = read('hosts', 'DigitalBrain.Quickstart.Host', 'Program.cs')
  const appHost = read('hosts', 'DigitalBrain.Quickstart.AppHost', 'AppHost.cs')
  const fixture = read('tests', 'DigitalBrain.Quickstart.Tests', 'QuickstartFixture.cs')

  assert.match(quickstart, /^---\r?\ntitle: Quickstart\r?\n---/)
  assert.ok(contract.includes('interface IGreeter'), 'the contracts package must expose IGreeter')
  assert.ok(synapses.includes('record SayHello'), 'the contracts package must expose SayHello')
  assert.ok(synapses.includes('record Greeted'), 'the contracts package must expose Greeted')
  assert.ok(module.includes('partial class QuickstartModule'), 'the runtime must expose its compiled module')
  assert.ok(host.includes('AddDigitalBrain()'), 'the compiled host must install the kernel')
  assert.match(appHost, /"quickstart"/, 'AppHost brain identity remains quickstart')
  assert.match(
    appHost,
    /AddDigitalBrain\(\s*(?:Brain|"quickstart")\s*\)/,
    'AppHost must own infrastructure via AddDigitalBrain')
  assert.ok(appHost.includes('AddModule<QuickstartModule>()'), 'AppHost must select the compiled module')
  assert.ok(fixture.includes('AddModule<QuickstartModule>()'), 'tests must select the same compiled module')
  assert.ok(quickstart.includes('interface IGreeter'), 'the quickstart must show the real contract')
  assert.ok(quickstart.includes('SendAsync<IGreeter>'), 'the quickstart must show the real client call')
  assert.ok(quickstart.includes('IDigitalBrain brain'), 'the endpoint must receive the client through DI')
  assert.ok(quickstart.includes('.WithReference(brain.AsClient())'), 'AppHost must project a client reference')
  assert.doesNotMatch(quickstart, /Host\.CreateApplicationBuilder|GetRequiredService|host\.StartAsync/)
  assert.match(quickstart, /not a product Behavior install/)
})

test('the specification describes the retained test tiers', () => {
  const specification = read('docs', 'specification.md')

  assert.match(specification, /# Specification/)
  assert.match(specification, /DigitalBrain\.Quickstart\.Tests/)
  assert.match(specification, /DigitalBrain\.Time\.Tests/)
  assert.match(specification, /DigitalBrain\.HostTests/)
  assert.doesNotMatch(specification, /DigitalBrain\.Simulations/)
  assert.doesNotMatch(specification, /ModuleDriver/)
})

test('every shipped package is in the table, and the boundary is stated', () => {
  const packages = read('docs', 'packages.md')
  const packableSource = read('tests', 'DigitalBrain.Tests', 'Packages', 'PackageInventory.cs')
  const consts = Object.fromEntries(
    [...packableSource.matchAll(/internal const string (\w+) = "([^"]+)"/g)].map(match => [match[1], match[2]]))
  const packableBlock = packableSource.match(/static readonly string\[\] Packable\s*=\s*\[([\s\S]*?)\];/)
  assert.ok(packableBlock, 'PackageInventory must declare Packable')
  const packable = [...packableBlock[1].matchAll(/\b([A-Z][A-Za-z0-9]*)\b/g)]
    .map(match => consts[match[1]])
    .filter(Boolean)
  assert.ok(packable.length >= 18, `expected the packable list, found ${packable.length}`)

  for (const name of packable) {
    assert.ok(packages.includes(`\`${name}\``), `the table must list ${name}`)
  }

  assert.ok(packages.includes('`DigitalBrain`'), 'the table must list the metapackage')
  assert.match(packages, /Model-provider SDKs live only in `DigitalBrain\.Modules\.AI`/)
  assert.match(packages, /does \*\*not\*\* reference `DigitalBrain\.Kernel`/)
  assert.match(packages, /refuses to start/i)
  assert.match(packages, /namespace and type name are the model identity/i)
  assert.match(packages, /openai-api-key/)
  assert.match(packages, /purpose-bound durable encryption/i)
  assert.match(packages, /southbound/i)
  assert.match(packages, /northbound/i)
})

test('the contributing guide states the gate and the non-negotiable rules', () => {
  const contributing = read('docs', 'contributing.md')

  assert.match(contributing, /dotnet test \.\\DigitalBrain\.slnx -c Release/)
  assert.match(contributing, /--filter/)
  assert.match(contributing, /Comments are forbidden/i)
  assert.match(contributing, /Tier 0/)
  assert.match(contributing, /Tier 1/)
  assert.match(contributing, /Tier 2/)
  assert.match(contributing, /digitalbrain\.tech/)
  assert.match(contributing, /docs-pages\.yml/)
  assert.match(contributing, /GitHub Actions/)
  assert.match(contributing, /CI and CD/)
  assert.match(contributing, /pull requests only/i)
  assert.match(contributing, /does \*\*not\*\* wait on the framework/)
  assert.match(contributing, /dependabot\.yml/)
})

test('the open debts are disclosed rather than buried', () => {
  const architecture = read('docs', 'architecture.md')

  assert.match(architecture, /trusted cluster peer/)
  assert.match(architecture, /Journal history is bounded/)
  assert.match(architecture, /Effectively-once processing is also windowed/)
  assert.match(architecture, /FIFO per target/)
  assert.match(architecture, /Delivery ordering/)
  assert.match(architecture, /Broadcast addressing/)
  assert.match(architecture, /handler \*\*types\*\*/)
  assert.match(architecture, /timeline stream/)
  assert.match(architecture, /AsClient/)
  assert.match(architecture, /DevUI/)

  assert.match(read('docs', 'packages.md'), /not\*\* an authentication boundary/)
})

test('the ratified rules survive as a checklist', () => {
  const architecture = read('docs', 'architecture.md')

  const sectionStart = /^## 9\. Ratified rules$/m.exec(architecture)
  assert.ok(sectionStart, 'the "## 9. Ratified rules" heading must exist')

  const afterSectionStart = architecture.slice(sectionStart.index + sectionStart[0].length)
  const sectionEnd = /^## 10\..*$/m.exec(afterSectionStart)
  assert.ok(sectionEnd, 'the "## 10." heading that closes section 9 must exist')

  const ratifiedRulesSection = afterSectionStart.slice(0, sectionEnd.index)
  assert.ok(
    ratifiedRulesSection.trim().length > 0,
    'the ratified rules section between "## 9." and "## 10." must not be empty')

  const ruleNumbers = [...ratifiedRulesSection.matchAll(/^(\d+)\. \S/gm)]
    .map(match => Number.parseInt(match[1], 10))
  assert.ok(ruleNumbers.length > 0, 'the ratified rules section must contain numbered rules')
  assert.deepEqual(
    ruleNumbers,
    Array.from({ length: ruleNumbers.length }, (_, index) => index + 1),
    'the ratified rules must remain contiguous from 1 through the current final rule')

  for (const rejected of ['Ical.Net', 'Durable Extension', 'model tier', 'raw invoke']) {
    assert.ok(architecture.includes(rejected), `the rejected list must name ${rejected}`)
  }
})

test('the lean runtime boundaries are explicit and current', () => {
  const architecture = read('docs', 'architecture.md')
  const diagram = read('docs', '.vitepress', 'theme', 'architecture-data.js')

  assert.match(architecture, /direct .*AgentSession/i)
  assert.match(architecture, /supervised .*checkpoint/i)
  assert.match(architecture, /Microsoft\.Extensions\.AI/)
  assert.match(architecture, /MAF types stay internal/)
  assert.match(architecture, /AI-to-Tasks\.Contracts/)
  assert.match(architecture, /southbound/)
  assert.match(architecture, /northbound/)
  assert.match(architecture, /named .*token/i)
  assert.match(architecture, /AddViteApp\("website", "\.\.\/\.\.\/docs"\)/)
  assert.match(architecture, /AppHost build.*CodeGraph/i)
  assert.doesNotMatch(architecture, /IMcpAuthorizationRedirect/)
  assert.doesNotMatch(architecture, /shared client factory/)
  assert.doesNotMatch(diagram, /salesforce-client-secret/)
  assert.doesNotMatch(diagram, /Auto\/Headless|Auto\/Desktop|\bAuto host\b/)
})

test('samples and compositions honesty — pre-rail logic, no Behavior install lies', () => {
  const readme = read('README.md')
  const packages = read('docs', 'packages.md')
  const architecture = read('docs', 'architecture.md')
  const specification = read('docs', 'specification.md')
  const quickstart = read('docs', 'quickstart.md')
  const map = read('docs', '.vitepress', 'theme', 'ArchitectureMap.vue')
  const diagram = read('docs', '.vitepress', 'theme', 'architecture-data.js')

  assert.match(readme, /samples\/\s+.*Compositions/)
  assert.match(readme, /pre-Behavior-rail|not installed Behaviors/)
  assert.doesNotMatch(readme, /package consumers/)

  assert.match(packages, /samples\/DigitalBrain\.Compositions/)
  assert.match(packages, /not installed Behaviors/)
  assert.match(packages, /pre-rail logic|pre-Behavior rail|Pre-Behavior rail/)
  assert.match(packages, /AccountEnrichment/)
  assert.match(packages, /not a composition/)

  assert.match(architecture, /Built \(OS compositions, pre-Behavior rail\)/)
  assert.match(architecture, /not the install rail|not installed Behaviors|pre-rail helpers/)
  assert.match(architecture, /IBehavior/)
  assert.match(architecture, /Runtime behavior \*\*install\*\* is designed and not yet built|Runtime behavior installation is designed and not yet built/)
  assert.match(architecture, /AccountEnrichmentSurface.*not.*Gmail/)

  assert.match(specification, /DigitalBrain\.Compositions\.Tests/)
  assert.match(specification, /not\s+installed Behaviors/)
  assert.match(quickstart, /not a product Behavior install/)

  assert.match(map, /designed · unbuilt/)
  assert.match(map, /samples\/DigitalBrain\.Compositions/)
  assert.match(map, /not installed Behaviors/)
  assert.doesNotMatch(map, /designed, not built/)
  assert.doesNotMatch(diagram, /\bAuto\/Headless\b|\bAuto\/Desktop\b/)
  assert.match(diagram, /no Auto/)
})

test('no page resurrects rejected v1 vocabulary', () => {
  const v1Identifiers = /INeuronKind|INeuronContract|NeuronGrain|NeuronProxy|\bBrain\.slnx|neuron_invoke/
  const rejectedArchitecture = new RegExp([
    'Model' + 'Tier',
    'Model' + 'Providers',
    'IModel' + 'CompletionService',
    'Ask' + 'ModelAsync',
    'With' + 'Model\\(',
    'AddAI' + 'Module',
    'AddDigitalBrain' + 'Models',
    'ChatModel' + 'Neuron',
    'Scripted' + 'Model',
    '\\bBrain' + 'Client\\b',
  ].join('|'))

  for (const page of contentPages) {
    assert.doesNotMatch(read('docs', page), v1Identifiers, `${page} must not mention v1 identifiers`)
    assert.doesNotMatch(read('docs', page), rejectedArchitecture, `${page} must not teach the rejected architecture`)
  }
  assert.doesNotMatch(read('docs', '.vitepress', 'config.mts'), v1Identifiers)
  assert.doesNotMatch(read('README.md'), v1Identifiers)
})

test('the repository README points at the v2 gate and story', () => {
  const readme = read('README.md')

  assert.match(readme, /neurons/i)
  assert.match(readme, /DigitalBrain\.slnx/)
  assert.doesNotMatch(readme, /kernel\/|edge\/|workspace\//)
})

test('generated VitePress and npm output stays out of git', () => {
  const gitignore = read('.gitignore')

  assert.match(gitignore, /^node_modules\/$/m)
  assert.match(gitignore, /^docs\/\.vitepress\/cache\/$/m)
  assert.match(gitignore, /^docs\/\.vitepress\/dist\/$/m)
  assert.doesNotMatch(gitignore, /^docs\/specification\.md$/m)
})
