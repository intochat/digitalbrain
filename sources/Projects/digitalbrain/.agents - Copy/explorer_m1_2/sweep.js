const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const workspaceRoot = 'e:\\digitalbrain';
const outputReportPath = path.join(__dirname, 'analysis.md');

try {
  console.log('Running git grep to find all occurrences of BrainOS...');
  // We run git grep -n -i "BrainOS" to find all occurrences case-insensitively, including line numbers.
  // We exclude the .agents directory using pathspec.
  const rawGrepOutput = execSync('git grep -n -i "BrainOS" -- ":!.agents" ":!.git" ":!**/bin" ":!**/obj" ":!**/node_modules"', {
    cwd: workspaceRoot,
    maxBuffer: 50 * 1024 * 1024, // 50MB buffer
    encoding: 'utf8'
  });

  const lines = rawGrepOutput.split(/\r?\n/);
  console.log(`Found ${lines.length} lines of matches in non-agent directories.`);

  // Structures to hold the classified occurrences
  const directoriesToRename = new Set();
  const projectsToRename = new Set();
  const namespacesAndUsings = [];
  const referenceFiles = [];
  const configFiles = [];
  const documentationFiles = [];
  const otherOccurrences = [];

  // Let's identify the directories that need to be renamed from directories with BrainOS in their path.
  // E.g. kernel/BrainOS.AppHost -> kernel/DigitalBrain.AppHost
  const addDirIfBrainOS = (filePath) => {
    const parts = filePath.split(/[/\\]/);
    parts.forEach(part => {
      if (/BrainOS/i.test(part)) {
        // Find the full relative path up to this directory
        const dirIndex = parts.indexOf(part);
        const relativeDirPath = parts.slice(0, dirIndex + 1).join('/');
        directoriesToRename.add(relativeDirPath);
      }
    });
  };

  lines.forEach(line => {
    if (!line.trim()) return;

    // Line format is usually: filename:lineNumber:lineContent
    const firstColon = line.indexOf(':');
    if (firstColon === -1) return;
    const secondColon = line.indexOf(':', firstColon + 1);
    if (secondColon === -1) return;

    const filename = line.substring(0, firstColon).replace(/\\/g, '/');
    const lineNumber = line.substring(firstColon + 1, secondColon);
    const lineContent = line.substring(secondColon + 1);

    addDirIfBrainOS(filename);

    if (filename.endsWith('.csproj') && /BrainOS/i.test(path.basename(filename))) {
      projectsToRename.add(filename);
    }

    const occurrenceInfo = {
      filename,
      lineNumber,
      lineContent: lineContent.trim()
    };

    // Classify line content
    const lowerContent = lineContent.toLowerCase();
    const isSourceCode = filename.endsWith('.cs');
    const isProjectOrSolution = filename.endsWith('.csproj') || filename.endsWith('.slnx') || filename.endsWith('.props') || filename.endsWith('.targets');
    const isConfig = filename.endsWith('.json') || filename.endsWith('.xml') || filename.endsWith('.yaml') || filename.endsWith('.yml');
    const isDoc = filename.endsWith('.md') || filename.endsWith('.txt');

    if (isSourceCode && (lowerContent.includes('namespace ') || lowerContent.includes('using '))) {
      namespacesAndUsings.push(occurrenceInfo);
    } else if (isProjectOrSolution) {
      referenceFiles.push(occurrenceInfo);
    } else if (isConfig) {
      configFiles.push(occurrenceInfo);
    } else if (isDoc) {
      documentationFiles.push(occurrenceInfo);
    } else {
      otherOccurrences.push(occurrenceInfo);
    }
  });

  // Convert Set to sorted Array for directories
  const sortedDirs = Array.from(directoriesToRename).sort((a, b) => {
    // Sort directories so deeper ones are renamed first, or parent ones first depending on strategy.
    // Sorting by length descending ensures we process subdirectories before their parents if we do physical rename.
    return b.length - a.length;
  });

  // Let's generate a beautiful markdown analysis report
  let mdContent = `# Codebase Sweep & Rename Strategy (BrainOS -> DigitalBrain)

## 1. Summary of Sweep Results
We swept the entire repository (excluding agent meta-folders and git repository artifacts) for all occurrences of the term \`BrainOS\` (case-insensitive). Here is the breakdown:

- **Directories to Rename:** ${directoriesToRename.size} unique paths
- **Project Files (.csproj) to Rename:** ${projectsToRename.size} unique project files
- **Namespace & Using Occurrences in Source Code:** ${namespacesAndUsings.length} lines
- **Project & Solution File References:** ${referenceFiles.length} lines
- **Configuration & Setting File References:** ${configFiles.length} lines
- **Documentation File References:** ${documentationFiles.length} lines
- **Other Code / Path Occurrences:** ${otherOccurrences.length} lines
- **Total Occurrences Found:** ${lines.length} lines

---

## 2. Directories to Rename
The following physical directory paths contain \`BrainOS\` in their folder names. Deeper directories must be renamed or handled carefully during refactoring:

| No. | Relative Path | Proposed New Path |
| --- | --- | --- |
${sortedDirs.map((dir, idx) => `| ${idx + 1} | \`${dir}\` | \`${dir.replace(/BrainOS/gi, 'DigitalBrain')}\` |`).join('\n')}

*Note: The directories are listed in descending order of path depth to ensure child directories are renamed before their parent directories, preventing path-not-found errors during scripted renames.*

---

## 3. Project Files (.csproj) to Rename
The following project files contain \`BrainOS\` in their names and must be renamed:

| No. | Project File Path | Proposed New Filename |
| --- | --- | --- |
${Array.from(projectsToRename).sort().map((proj, idx) => `| ${idx + 1} | \`${proj}\` | \`${path.basename(proj).replace(/BrainOS/gi, 'DigitalBrain')}\` |`).join('\n')}

---

## 4. Namespace and Using Directives in Source Code
Below is the list of C# files containing \`namespace BrainOS...\` or \`using BrainOS...\` statements:

| File Path | Line | Content |
| --- | --- | --- |
${namespacesAndUsings.slice(0, 100).map(item => `| \`${item.filename}\` | ${item.lineNumber} | \`${item.lineContent.replace(/`/g, '\\`').substring(0, 80)}\` |`).join('\n')}
${namespacesAndUsings.length > 100 ? `| *...and ${namespacesAndUsings.length - 100} more using/namespace statements...* | | |` : ''}

---

## 5. References in Project, Solution, and Build Files
Below is the list of references inside \`.csproj\`, \`.slnx\`, \`.props\`, and \`.targets\` files:

| File Path | Line | Content |
| --- | --- | --- |
${referenceFiles.map(item => `| \`${item.filename}\` | ${item.lineNumber} | \`${item.lineContent.replace(/`/g, '\\`').substring(0, 80)}\` |`).join('\n')}

---

## 6. References in Configuration and Dashboard Files
Below is the list of references inside configuration files (e.g. \`appsettings*.json\`, \`plugin.json\`, \`launchSettings.json\`, etc.):

| File Path | Line | Content |
| --- | --- | --- |
${configFiles.map(item => `| \`${item.filename}\` | ${item.lineNumber} | \`${item.lineContent.replace(/`/g, '\\`').substring(0, 80)}\` |`).join('\n')}

---

## 7. References in Documentation and Markdown Files
Below is the list of references inside markdown (\`.md\`) files:

| File Path | Line | Content |
| --- | --- | --- |
${documentationFiles.slice(0, 100).map(item => `| \`${item.filename}\` | ${item.lineNumber} | \`${item.lineContent.replace(/`/g, '\\`').substring(0, 80)}\` |`).join('\n')}
${documentationFiles.length > 100 ? `| *...and ${documentationFiles.length - 100} more documentation references...* | | |` : ''}

---

## 8. Other Code Occurrences
Other occurrences of \`BrainOS\` inside C# method bodies, comments, string literals, and .ino files:

| File Path | Line | Content |
| --- | --- | --- |
${otherOccurrences.slice(0, 150).map(item => `| \`${item.filename}\` | ${item.lineNumber} | \`${item.lineContent.replace(/`/g, '\\`').substring(0, 80)}\` |`).join('\n')}
${otherOccurrences.length > 150 ? `| *...and ${otherOccurrences.length - 150} more general occurrences...* | | |` : ''}

---

## 9. Safe Rename Execution Strategy for the Worker

To execute these renames safely without breaking compilation, the Worker should follow this step-by-step strategy:

### Phase 1: Preparation & Environment Check
1. Ensure the workspace is clean (\`git status\` should have no uncommitted changes).
2. Create a backup branch: \`git checkout -b feature/digitalbrain-rename\`.
3. Run \`dotnet build\` and \`dotnet test\` to verify that the project is completely green before any changes are made.

### Phase 2: In-File Text Renaming (The "Namespace First" Rule)
*Do not rename directories or files yet! First, update all occurrences inside the files so that the code continues to compile cleanly against the old structure, or is ready for the rename.*
1. **Source Code Renames**: Use global search-and-replace to change all case-sensitive occurrences of:
   - \`BrainOS\` -> \`DigitalBrain\`
   - \`brainos\` -> \`digitalbrain\` (in lowercase contexts, e.g. lower namespaces or paths if applicable)
2. **Project and Solution Reference Updates**: Update all project-to-project reference paths and solution file (\`DigitalBrain.slnx\`) entries to point to the *future* paths of the projects (e.g. change \`kernel/BrainOS.Core/BrainOS.Core.csproj\` to \`kernel/DigitalBrain.Core/DigitalBrain.Core.csproj\`).
3. **Configuration and Doc Updates**: Update settings, connection strings, environment variables, launchSettings, Docker, and Aspire configuration files to replace \`BrainOS\` with \`DigitalBrain\`.

### Phase 3: Project File Renaming (.csproj)
*Rename the actual project files on the filesystem before moving their directories.*
1. Rename every \`*BrainOS*.csproj\` file to its corresponding \`*DigitalBrain*.csproj\` equivalent in-place.
2. For example, rename \`kernel/BrainOS.Core/BrainOS.Core.csproj\` to \`kernel/BrainOS.Core/DigitalBrain.Core.csproj\`.

### Phase 4: Directory Path Renaming
*Rename the directories on the filesystem.*
1. Using a PowerShell script or manual commands, rename the physical folders in **depth-first order** (subdirectories first, then parent directories):
   - Rename \`kernel/BrainOS.Domains.Dynamic/BrainOS.Domains.Dynamic\` to \`kernel/BrainOS.Domains.Dynamic/DigitalBrain.Domains.Dynamic\`
   - Rename \`kernel/BrainOS.Domains.Dynamic/BrainOS.Domains.Dynamic.Contracts\` to \`kernel/BrainOS.Domains.Dynamic/DigitalBrain.Domains.Dynamic.Contracts\`
   - Then rename the parent folder \`kernel/BrainOS.Domains.Dynamic\` to \`kernel/DigitalBrain.Domains.Dynamic\`
   - Repeat for all other folders:
     - \`kernel/BrainOS.AppHost\` -> \`kernel/DigitalBrain.AppHost\`
     - \`kernel/BrainOS.Boot\` -> \`kernel/DigitalBrain.Boot\`
     - \`kernel/BrainOS.Core\` -> \`kernel/DigitalBrain.Core\`
     - \`kernel/BrainOS.Core.Hosting\` -> \`kernel/DigitalBrain.Core.Hosting\`
     - \`kernel/BrainOS.Core.SourceGen\` -> \`kernel/DigitalBrain.Core.SourceGen\`
     - \`kernel/BrainOS.Hosting\` -> \`kernel/DigitalBrain.Hosting\`
     - \`kernel/BrainOS.Kernel\` -> \`kernel/DigitalBrain.Kernel\`
     - \`kernel/BrainOS.Kernel.Contracts\` -> \`kernel/DigitalBrain.Kernel.Contracts\`
     - \`kernel/BrainOS.NeuronTesting\` -> \`kernel/DigitalBrain.NeuronTesting\`
     - \`kernel/BrainOS.ServiceDefaults\` -> \`kernel/DigitalBrain.ServiceDefaults\`
     - \`samples/BrainOS.Domains.Samples\` -> \`samples/DigitalBrain.Domains.Samples\`

### Phase 5: Verification & Compilation
1. Run \`dotnet build\` to see if Orleans codegen or standard compilation fails. Fix any namespace discrepancy.
2. Run \`dotnet test\` to execute all 121+ tests and confirm they pass successfully.
3. Test boot verification: \`dotnet run digitalbrain.cs\` and \`dotnet run testdigitalbrain.cs\`.
`;

  fs.writeFileSync(outputReportPath, mdContent, 'utf8');
  console.log(`Successfully generated the analysis report at: ${outputReportPath}`);

} catch (error) {
  console.error('Error running sweep:', error);
}
