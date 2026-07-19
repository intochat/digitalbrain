import { readdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const toolsDirectory = dirname(fileURLToPath(import.meta.url))
const websiteRoot = resolve(toolsDirectory, '..')
const simulations = resolve(websiteRoot, '..', 'tests', 'DigitalBrain.Simulations')

const featureFiles = readdirSync(simulations)
  .filter(entry => entry.endsWith('.feature'))
  .sort()

const titleOf = source => {
  const heading = source.split('\n').find(line => line.trimStart().startsWith('Feature:'))
  return heading ? heading.trim().slice('Feature:'.length).trim() : 'Untitled'
}

const sections = featureFiles.map(file => {
  const source = readFileSync(join(simulations, file), 'utf8').trimEnd()
  return `## ${titleOf(source)}\n\n\`\`\`gherkin\n${source}\n\`\`\`\n`
})

const page = `---
title: Specification
---

# Specification

Every behaviour DigitalBrain guarantees is written as a Tier-1 simulation: a scenario fired into a real
three-silo in-process Orleans cluster, asserted against real journals. These files are not
illustrations of the framework — they are the executable specification, and the root gate
\`dotnet test .\\DigitalBrain.slnx -c Release\` fails if any of them stops holding.

This page is generated from \`tests/DigitalBrain.Simulations/*.feature\` at build time, so it cannot
drift from what actually passes.

${sections.join('\n')}`

writeFileSync(join(websiteRoot, 'specification.md'), page, 'utf8')

console.log(`rendered ${featureFiles.length} feature files into specification.md`)
