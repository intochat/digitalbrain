# Assistant Project Consolidation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Merge `Assistant.csproj` (grain contracts) into `Assistant.Silo` and delete empty stub projects, reducing 5 projects to 2.

**Architecture:** Move 20 interface/model files into a `Contracts/` subfolder inside `Assistant.Silo`. Update project references and solution file. No namespace changes needed.

**Tech Stack:** .NET 11.0, Orleans 10.0, Aspire 13.1.2

---

### Task 1: Create Contracts folder structure in Assistant.Silo

**Files:**
- Create: `src/Assistant/Assistant.Silo/Contracts/` (directory)
- Create: `src/Assistant/Assistant.Silo/Contracts/Agents/` (directory)
- Create: `src/Assistant/Assistant.Silo/Contracts/Agents/Models/` (directory)
- Create: `src/Assistant/Assistant.Silo/Contracts/Telegram/` (directory)
- Create: `src/Assistant/Assistant.Silo/Contracts/Telegram/Models/` (directory)
- Create: `src/Assistant/Assistant.Silo/Contracts/Voice/` (directory)
- Create: `src/Assistant/Assistant.Silo/Contracts/Events/` (directory)

**Step 1: Create all directories**

```bash
mkdir -p src/Assistant/Assistant.Silo/Contracts/{Agents/Models,Telegram/Models,Voice,Events}
```

**Step 2: Commit**

```bash
git add src/Assistant/Assistant.Silo/Contracts/
git commit -m "chore: create Contracts folder structure in Assistant.Silo"
```

---

### Task 2: Move contract files into Assistant.Silo/Contracts

**Files:**
- Move: All 20 source files from `src/Assistant/Assistant/` → `src/Assistant/Assistant.Silo/Contracts/`

**Step 1: Move all files preserving folder structure**

```bash
# Root contracts
cp src/Assistant/Assistant/IUserAgent.cs src/Assistant/Assistant.Silo/Contracts/

# Agents
cp src/Assistant/Assistant/Agents/IFlightSearch.cs src/Assistant/Assistant.Silo/Contracts/Agents/
cp src/Assistant/Assistant/Agents/IGeneralAssistant.cs src/Assistant/Assistant.Silo/Contracts/Agents/
cp src/Assistant/Assistant/Agents/INotification.cs src/Assistant/Assistant.Silo/Contracts/Agents/
cp src/Assistant/Assistant/Agents/IPlaceSearch.cs src/Assistant/Assistant.Silo/Contracts/Agents/
cp src/Assistant/Assistant/Agents/IPriceTracker.cs src/Assistant/Assistant.Silo/Contracts/Agents/
cp src/Assistant/Assistant/Agents/IStaySearch.cs src/Assistant/Assistant.Silo/Contracts/Agents/
cp src/Assistant/Assistant/Agents/ITravelAssistant.cs src/Assistant/Assistant.Silo/Contracts/Agents/
cp src/Assistant/Assistant/Agents/IWeather.cs src/Assistant/Assistant.Silo/Contracts/Agents/
cp src/Assistant/Assistant/Agents/Models/SearchModels.cs src/Assistant/Assistant.Silo/Contracts/Agents/Models/

# Events
cp src/Assistant/Assistant/Events/PriceAlert.cs src/Assistant/Assistant.Silo/Contracts/Events/
cp src/Assistant/Assistant/Events/WeatherAlert.cs src/Assistant/Assistant.Silo/Contracts/Events/

# Telegram
cp src/Assistant/Assistant/Telegram/ITelegram.cs src/Assistant/Assistant.Silo/Contracts/Telegram/
cp src/Assistant/Assistant/Telegram/ITelegramDashboardGrain.cs src/Assistant/Assistant.Silo/Contracts/Telegram/
cp src/Assistant/Assistant/Telegram/ITelegramUser.cs src/Assistant/Assistant.Silo/Contracts/Telegram/
cp src/Assistant/Assistant/Telegram/Models/DashboardModels.cs src/Assistant/Assistant.Silo/Contracts/Telegram/Models/
cp src/Assistant/Assistant/Telegram/Models/TelegramModels.cs src/Assistant/Assistant.Silo/Contracts/Telegram/Models/

# Voice
cp src/Assistant/Assistant/Voice/IAudioConverterGrain.cs src/Assistant/Assistant.Silo/Contracts/Voice/
cp src/Assistant/Assistant/Voice/IVoiceGrain.cs src/Assistant/Assistant.Silo/Contracts/Voice/
cp src/Assistant/Assistant/Voice/IWhisperGrain.cs src/Assistant/Assistant.Silo/Contracts/Voice/
```

**Step 2: Verify file count matches**

```bash
find src/Assistant/Assistant.Silo/Contracts -name "*.cs" | wc -l
# Expected: 20
```

**Step 3: Commit**

```bash
git add src/Assistant/Assistant.Silo/Contracts/
git commit -m "chore: copy contract files into Assistant.Silo/Contracts"
```

---

### Task 3: Update Assistant.Silo.csproj references

**Files:**
- Modify: `src/Assistant/Assistant.Silo/Assistant.Silo.csproj`

The contracts project (`Assistant.csproj`) has these dependencies that Silo needs:
- `Microsoft.Orleans.Core` — already included transitively via `Microsoft.Orleans.Server`
- `Microsoft.Orleans.Sdk` — already included transitively via `Microsoft.Orleans.Server`
- `Microsoft.Orleans.Serialization` — already included transitively via `Microsoft.Orleans.Server`
- ProjectReference to `IAW.Core` — **must be added directly** since removing Assistant.csproj removes this transitive path

**Step 1: Update csproj**

In `src/Assistant/Assistant.Silo/Assistant.Silo.csproj`:

Remove this line from ProjectReferences:
```xml
    <ProjectReference Include="..\Assistant\Assistant.csproj" />
```

Add IAW.Core ProjectReference (in the same ItemGroup):
```xml
    <ProjectReference Include="..\..\AI\IAW.Core\IAW.Core.csproj" />
```

Result ProjectReferences should be:
```xml
  <ItemGroup>
    <ProjectReference Include="..\Assistant.MiniApp.Ui\Assistant.MiniApp.Ui.csproj" />
    <ProjectReference Include="..\..\AI\IAW.Core\IAW.Core.csproj" />
    <ProjectReference Include="..\..\TripRadar\TripRadar\TripRadar.csproj" />
  </ItemGroup>
```

**Step 2: Build to verify**

```bash
dotnet build src/Assistant/Assistant.Silo/Assistant.Silo.csproj
```

Expected: Build succeeded (namespaces remain unchanged, so all `using` statements still resolve)

**Step 3: Commit**

```bash
git add src/Assistant/Assistant.Silo/Assistant.Silo.csproj
git commit -m "chore: update Silo references - drop Assistant.csproj, add IAW.Core directly"
```

---

### Task 4: Update solution file

**Files:**
- Modify: `TripRadar.slnx`

**Step 1: Remove the Assistant.csproj entry**

In `TripRadar.slnx`, remove this line from the `/Assistant/` folder:
```xml
    <Project Path="src/Assistant/Assistant/Assistant.csproj" />
```

**Step 2: Commit**

```bash
git add TripRadar.slnx
git commit -m "chore: remove Assistant contracts project from solution"
```

---

### Task 5: Delete old projects

**Files:**
- Delete: `src/Assistant/Assistant/` (entire directory)
- Delete: `src/Assistant/Assistant.MiniApp.Api/` (empty stub)
- Delete: `src/Assistant/Assistant.TelegramMiniApp/` (empty stub)

**Step 1: Delete directories**

```bash
rm -rf src/Assistant/Assistant/
rm -rf src/Assistant/Assistant.MiniApp.Api/
rm -rf src/Assistant/Assistant.TelegramMiniApp/
```

**Step 2: Commit**

```bash
git add -A src/Assistant/Assistant/ src/Assistant/Assistant.MiniApp.Api/ src/Assistant/Assistant.TelegramMiniApp/
git commit -m "chore: delete Assistant contracts project and empty stubs"
```

---

### Task 6: Full build and Aspire verification

**Step 1: Full build**

```bash
dotnet build src/Aspire/Aspire.csproj
```

Expected: Build succeeded, 0 errors

**Step 2: Aspire run smoke test**

```bash
dotnet run --project src/Aspire/Aspire.csproj
```

Expected: Aspire starts, dashboard loads, `assistant-host` resource appears

**Step 3: Verify with Aspire MCP tools**

Use `mcp__aspire__list_resources` to confirm `assistant-host` is running.

**Step 4: Final commit (if any fixups needed)**

```bash
git commit -m "chore: assistant project consolidation complete"
```
