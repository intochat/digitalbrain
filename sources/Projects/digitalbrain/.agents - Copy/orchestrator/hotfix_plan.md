# Hotfix Plan: InoTestGenerator Escaping & Collision Resolution

## 1. Objectives
Address critical robustness issues identified by Reviewer 2:
1. **Special Character String Escaping Flaw [Critical]**: Prevent compile-time `CS1009` errors when scenario names contain backslashes (`\`) or valid escape characters (e.g. `\t`).
2. **Duplicate Scenario DisplayName Collision [Major]**: Append ` [#{i}]` suffixes to duplicate scenario names to match runtime test adapter labels in `InoScenarioProjection.cs`.
3. **Potential NullReferenceException on Empty Directories [Minor]**: Guard `Path.GetDirectoryName(inoSource.FullPath)` against returning `null`.

---

## 2. Technical Modifications in `InoTestGenerator.cs`

### A. Guard against null Directory Name
Replace line 136:
```csharp
var rootDir = Path.GetDirectoryName(inoSource.FullPath).Replace("\\", "/");
```
With:
```csharp
var dir = Path.GetDirectoryName(inoSource.FullPath);
var rootDir = dir is null ? "" : dir.Replace("\\", "/");
```

### B. Detect Duplicate Scenario Names
Before generating the `[Fact]` methods, calculate the set of duplicate scenario names (identical to how it is done in `InoScenarioProjection.cs`):
```csharp
var duplicateNames = doc.Scenarios
    .GroupBy(s => s.Name, StringComparer.Ordinal)
    .Where(g => g.Count() > 1)
    .Select(g => g.Key)
    .ToHashSet(StringComparer.Ordinal);
```

### C. Verbatim String Escaping & Suffixing in Generated Facts
For normal scenarios, replace lines 167-183 with:
```csharp
for (int i = 0; i < doc.Scenarios.Count; i++)
{
    var scenario = doc.Scenarios[i];
    var escapedName = scenario.Name.Replace("\"", "\"\"");
    var displayName = duplicateNames.Contains(scenario.Name)
        ? $"{scenario.Name} [#{i}]"
        : scenario.Name;
    var escapedDisplayName = displayName.Replace("\"", "\"\"");

    sb.AppendLine($"    [Fact(DisplayName = @\"{fileName} :: {escapedDisplayName}\")]");
    sb.AppendLine($"    public async Task Scenario_{i}()");
    sb.AppendLine("    {");
    sb.AppendLine("        var catalog = GetCatalog();");
    sb.AppendLine($"        var report = await global::DigitalBrain.InoLang.TestRunner.InoScenarioProjection.RunAsync(");
    sb.AppendLine($"            @\"{rootDir}\", \"{fileName}\", @\"{escapedName}\", \"scenario:{i}\",");
    sb.AppendLine($"            catalog, global::Xunit.TestContext.Current.CancellationToken);");
    sb.AppendLine("        global::Xunit.Assert.True(report.Passed, report.Message);");
    sb.AppendLine("    }");
    sb.AppendLine();
}
```

And also use verbatim string literals for the diagnostic compile-error and no-scenarios display names:
```csharp
sb.AppendLine($"    [Fact(DisplayName = @\"{fileName} :: <compile error>\")]");
```
And:
```csharp
sb.AppendLine($"    [Fact(DisplayName = @\"{fileName} :: <no scenarios>\")]");
```

---

## 3. Verification Criteria
1. Build `BrainOS.Fast.slnx` successfully with zero warnings/errors.
2. Confirm all 408 fast tests pass.
3. Validate that travel domain tests pass.
4. Execute `GeneratorStressTester` tests:
   ```powershell
   dotnet run --project challenger_tests/GeneratorStressTester/GeneratorStressTester.csproj
   ```
   Ensure it passes all checks, including the duplicate name check.
