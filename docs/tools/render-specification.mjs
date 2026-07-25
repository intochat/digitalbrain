import { readFileSync, writeFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const toolsDirectory = dirname(fileURLToPath(import.meta.url))
const docsRoot = resolve(toolsDirectory, '..')
const specificationPath = resolve(docsRoot, 'specification.md')

const page = readFileSync(specificationPath, 'utf8')
if (!page.includes('# Specification')) {
  throw new Error('docs/specification.md is missing its Specification heading.')
}

writeFileSync(specificationPath, page.endsWith('\n') ? page : `${page}\n`)
