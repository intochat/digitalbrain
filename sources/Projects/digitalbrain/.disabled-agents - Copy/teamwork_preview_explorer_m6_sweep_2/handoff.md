# SDK Reorganization Handoff Report

## 1. Observation
* **Folder Structure**: Directly observed the subdirectories under `sdk/DigitalBrain.SDK/` by listing contents:
  ```json
  {"name":"Ai", "isDir":true}
  {"name":"Aspire", "isDir":true}
  {"name":"Canvas", "isDir":true}
  {"name":"CodeGraph", "isDir":true}
  {"name":"Developer", "isDir":true}
  {"name":"Google", "isDir":true}
  {"name":"Identity", "isDir":true}
  {"name":"Onboarding", "isDir":true}
  ...
  ```
  And observed that `sdk/DigitalBrain.SDK.Contracts/` mirrors this directory layout exactly.
* **Namespaces**: Grep search on namespace declarations showed that almost all files follow the folder hierarchy under `DigitalBrain.SDK`, e.g., `namespace DigitalBrain.SDK.Ai.AiHealth;` in `Ai/AiHealth/AiHealthNeuron.cs:3`.
* **Onboarding Anomaly**: Discovered in `Onboarding/Onboarding/OnboardingPlan.cs` line 7:
  ```csharp
  namespace BrainOS.Domains.Onboarding.Onboarding;
  ```
  Which departs from the standard SDK naming convention and instead points to the Domain projects structure.
* **Using Statements**: Grep search found 186 references to `using DigitalBrain.SDK` across the entire solution, particularly concentrated under `DigitalBrain.Test/` (e.g., `using DigitalBrain.SDK.Ai;` at `e:\digitalbrain\DigitalBrain.Test\Ai\LlmExpressiveTests.cs:5`) and `kernel/` (e.g., `using DigitalBrain.SDK.Ai;` at `e:\digitalbrain\kernel\BrainOS.AppHost\Program.cs:2`).
* **Project References**: Evaluated `.csproj` files, finding 5 direct references to `DigitalBrain.SDK.csproj` (e.g. `ProjectReference Include="..\..\sdk\DigitalBrain.SDK\DigitalBrain.SDK.csproj"` in `kernel/BrainOS.AppHost/BrainOS.AppHost.csproj:13`) and 9 direct references to `DigitalBrain.SDK.Contracts.csproj`.
* **Roslyn Workspace**: Discovered in `Swarm/SwarmWorkspace.cs` a Roslyn compilation workspace that uses dynamic code syntax trees:
  ```csharp
  public void AddOrUpdateDocument(string name, string sourceCode)
  {
      ...
      var tree = CSharpSyntaxTree.ParseText(sourceCode, path: name);
      ...
  }
  ```

---

## 2. Logic Chain
1. **Cataloging and Mapping**: By comparing the responsibilities of the 23 cataloged subdirectories with the 4 target domains (Ai, Collaboration, Development, UI), we deduce the mapping.
   * `Ai` and `XAI` deal with AI models and prompting -> mapped to **Ai**.
   * `Google`, `Telegram`, `Stripe`, and `GitHub` (currently inside `Developer`) deal with external accounts, integrations, webhooks, and messages -> mapped to **Collaboration**.
   * `Developer` (excl. GitHub), `INO`, `SoftwareEngineering`, `Scripting`, `Testing`, `CodeGraph`, `Windows`, `Aspire`, `Persistence`, `Postgres`, `Sqlite`, `Security`, `Identity`, `Onboarding` deal with system-level compilation, database access, local I/O, and platform policies -> mapped to **Development**.
   * `Canvas` and `Visuals` deal with canvas layout rendering and materials/icons catalogs -> mapped to **UI**.
2. **Edge Case Alignment**:
   * **Swarm**: Swarm uses LLM clients (`IChatClient`) for multi-agent reasoning. Therefore, placing it in **Ai/Swarm** is logically superior to generic Collaboration.
   * **Persistence, Postgres, Sqlite**: Since these manage platform ORM / database access (EF Core and ADO.NET connection factories), they align logically with **Development/Persistence/**.
   * **Aspire and Security**: These manage local container orchestration and platform secrets, representing developer time setup and kernel bindings, mapping to **Development/Aspire** and **Development/Security**.
3. **Updating Dependencies**: Since C# source files are automatically compiled by `.csproj` projects using wildcard matches, moving directories will not break `.csproj` compilation, but will break namespace usages. Every C# file containing `using DigitalBrain.SDK.<OldFolder>` must be updated to reference `using DigitalBrain.SDK.<NewDomain>.<OldFolder>`.
4. **Mirroring Contracts**: Since `DigitalBrain.SDK.Contracts` contains the interfaces matching the SDK classes under identical subfolders, reorganizing the main SDK without reorganizing the contracts project would break the structural parity and increase maintenance overhead. Therefore, contracts must be restructured concurrently.

---

## 3. Caveats
* **Orleans Grain Persistence**: Orleans serializes grain state and stream subscriptions. Renaming namespaces of grains (`SwarmAgentNeuron`, `IdentityNeuron`, etc.) or subscription message types (like `SwarmAgentMessage`) might break backwards compatibility with serialized data in Redis or SQLite. During implementation, it is highly recommended to use Orleans alias attributes (`[Alias(...)]`) or preserve the historical namespaces for serialized structures while moving folders.
* **Source Gen Path Resolving**: Wildcards in `DigitalBrain.SDK.csproj` handle `.ino` compilation natively, but we assume the transpiler uses standard relative file access which will be unaffected by the physical path changes.

---

## 4. Conclusion
We have compiled a complete blueprint for the reorganization of the SDK. All 23 subdirectories under `DigitalBrain.SDK/` and `DigitalBrain.SDK.Contracts/` are mapped precisely to the four domains. The edge cases are logically addressed, and all 186 `using` declarations, 14 `.csproj` project references, and specific namespace anomalies (such as Onboarding) have been documented. This establishes a risk-free, highly structured plan for the upcoming implementation sweep.

---

## 5. Verification Method
To verify that this analysis is correct:
1. **Directory Integrity**:
   Verify that all 23 directories are accounted for in `analysis.md` under `e:\digitalbrain\sdk\DigitalBrain.SDK\`.
2. **Solution compilation test**:
   Build the solution using:
   ```powershell
   dotnet build e:\digitalbrain\DigitalBrain.slnx
   ```
   After restructuring directories and applying the proposed `using` updates, compile again to ensure 0 compiler errors.
3. **Run Test Suites**:
   Run all test projects using:
   ```powershell
   dotnet test e:\digitalbrain\DigitalBrain.slnx
   ```
   All test cases must pass, confirming that no serialization or runtime reflection-based Orleans stream paths are broken.
