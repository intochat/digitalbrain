import assert from 'node:assert/strict'
import test from 'node:test'
import { contentPages, read } from './support.mjs'

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
