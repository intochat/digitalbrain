## 2026-05-23T16:19:08Z

<user_information>
The USER's OS version is windows.
The user has 1 active workspaces, each defined by a URI and a CorpusName. Multiple URIs potentially map to the same CorpusName. The mapping is shown as follows in the format [URI] -> [CorpusName]:
e:\digitalbrain -> LeftTwixWand/digitalbrain
Code relating to the user's requests should be written in the locations listed above. Avoid writing project code files to tmp, in the .gemini dir, or directly to the Desktop and similar folders unless explicitly asked.
App Data Directory: C:\Users\vhorb\.gemini\antigravity
Conversation ID: ac2e0fc4-edab-4821-abd6-ae18bbf33c0c
</user_information><skills>
Available skills:
...
</skills><subagent_reminder>
You are running as a subagent, invoked by a caller agent (name: "main agent", id: "467782dd-0df6-400e-9cdd-0cae96263d7f"). You MUST use send_message to communicate all results, reports, and updates back to the caller. Your response is NOT automatically relayed — if you do not call send_message, the caller will only know that you have gone idle. Always use the caller's id as the Recipient and "main agent" as the RecipientName.

Text you generate outside of send_message will NOT be seen by the caller, so keep them brief. Put all important information — findings, summaries, conclusions — into your send_message calls instead. You can also share files by including their absolute paths in your message; the caller can then read them directly.
</subagent_reminder><USER_REQUEST>
You are the Codebase Explorer for the architectural and terminology sweep.
Your task is to analyze the codebase and map out all resources required for our restructuring:

1. **Locate Seam Terminology & Renaming Targets**:
   - Find every file, class, interface, method, property, and namespace containing the word 'Seam' or 'Seams'.
   - Specifically locate classes like `PredicateSeamBinding`, `SeamCatalogInvariantHostedService`, `SeamCatalogInvariantVerifier` and note their paths, constructors, and usage across the projects (including SDK, Kernel, and Tests).

2. **Locate Kernel Integration and DI Setup**:
   - Inspect `BrainOS.Kernel` (e.g. `Program.cs`, Grains, Services) to identify any heavy integration routines (database operations, AI prompting, OS runtime hooks).
   - Document how these are structured and where they can be decoupled into abstract interfaces.

3. **Locate PostgreSQL Integration**:
   - Find all existing PostgreSQL persistence, Entity Framework, or Dapper configurations in the codebase.
   - Detail where and how Orleans persistence is registered, and how we can support named/keyed DB connections dynamically resolved via Orleans Keyed DI at runtime (e.g. `users_db`, `analytics_db`).
   - Identify existing synapse entities or schema mappings.

4. **Locate Orleans Memory Streams**:
   - Find all existing Orleans Stream Provider configurations in the cluster.
   - Detail where we can configure Orleans Memory Streams to act as virtual synapse channels for Neuron Swarms.

5. **Locate InoLang DSL and Neuron Creator Schema**:
   - Locate the InoLang compiler, lexer, parser, and the Neuron Creator JSON schema.
   - Identify where we specify inputs, outputs, and parameters, and how we can simplify the schema and DSL.

6. **Locate Core Projects vs Connector Modules**:
   - Map all projects in the solution (`DigitalBrain.slnx`).
   - Classify each project as `DigitalBrain.Core` (open-source: compiler, parser, state machine, SDK core) or closed-source proprietary connector modules (AI, Aspire, Google, Sqlite, Windows, Mcp, Canvas, Visuals, Identity).

7. **Verify Build & Tests**:
   - Run `dotnet build` and count all existing tests in the solution.

Create your working directory under `.agents/teamwork_preview_explorer_sweep_1`.
Generate `analysis.md` and `handoff.md` (which MUST follow the Handoff Protocol: Observation, Logic Chain, Caveats, Conclusion, Verification Method) in your working directory.
When finished, send a completion message with the absolute paths to your reports.
</USER_REQUEST>
