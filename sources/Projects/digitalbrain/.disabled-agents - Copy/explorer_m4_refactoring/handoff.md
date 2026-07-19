# Handoff Report: Milestone 4 xAI MCP Integration & Verification

## 1. Observation
Direct observations of the codebase reveal the following specific locations and behaviors:
* **Grok Neuron Grain**: `sdk/DigitalBrain.SDK/Ai/Llm/Neuron/Grok.cs`
  * Lines 43-46:
    ```csharp
    if (string.IsNullOrEmpty(apiKey))
    {
        apiKey = Environment.GetEnvironmentVariable("XAI_API_KEY") ?? "mock-xai-api-key";
    }
    ```
    Directly falls back to `XAI_API_KEY` in the Silo environment, but defaults to `"mock-xai-api-key"` if empty.
* **Grok Provider Factory**: `sdk/DigitalBrain.SDK/Ai/Llm/Providers/GrokProviderFactory.cs`
  * Lines 11-12:
    ```csharp
    public bool IsConfigured(IConfiguration config)
        => !string.IsNullOrEmpty(config["DigitalBrain:Ai:GrokApiKey"]);
    ```
  * Lines 16-17:
    ```csharp
    public IChatClient CreateClient(LlmModel model, IConfiguration config)
    {
        var apiKey = config["DigitalBrain:Ai:GrokApiKey"];
    ```
    Does **not** check the environment variable `XAI_API_KEY` at all.
* **Silo real provider registration**: `sdk/DigitalBrain.SDK/Ai/DigitalBrainAiBridge.cs`
  * Line 89:
    ```csharp
    if (!factory.IsConfigured(configuration)) continue;
    ```
    If `IsConfigured` is false, it skips registering the keyed chat client `IChatClient` under `model.ServiceKey`.
* **Aspire AppHost Secret Parameter Initialization**: `kernel/DigitalBrain.Hosting/DigitalBrain/DigitalBrainResource.cs`
  * Lines 84-87:
    ```csharp
    if (AppBuilder.Configuration[$"Parameters:{parameterName}"] is null)
    {
        AppBuilder.Configuration[$"Parameters:{parameterName}"] = "placeholder";
    }
    ```
    If not specified in local AppHost configuration, `grok-api-key` gets hardcoded to `"placeholder"`. The AppHost ignores the `XAI_API_KEY` set in the shell's environment.
* **Silo Environment Injection**: `kernel/DigitalBrain.Hosting/DigitalBrain/AiDomainBuilder.cs`
  * Lines 76-77:
    ```csharp
    if (DigitalBrain.Secrets.TryGetValue("grok-api-key", out var grok))
        silo.WithEnvironment("DigitalBrain__Ai__GrokApiKey", grok);
    ```
    Because `grok-api-key` is bound to the parameter, if the parameter is `"placeholder"`, the silo process runs with `DigitalBrain__Ai__GrokApiKey` set to `"placeholder"`.
* **MCP Tool Gateway Server Setup**: `sdk/DigitalBrain.SDK.Mcp/DigitalBrain.SDK.Mcp/Program.cs`
  * Lines 27-30:
    ```csharp
    builder.Services
        .AddMcpServer()
        .WithHttpTransport(options => options.Stateless = true)
        .WithTools<BrainTools>();
    ```
* **MCP Tool Gateway Tools**: `sdk/DigitalBrain.SDK.Mcp/DigitalBrain.SDK.Mcp/Tools/BrainTools.cs`
  * Line 16: `[McpServerTool(Name = "brain")]` which watches the gRPC home feed stream and folds cards via `CardFold.Reduce`.
  * Line 86: `[McpServerTool(Name = "list_neurons")]` which captures nodes from `brainWatch.SnapshotAsync`.

---

## 2. Logic Chain
1. **The Issue**: If a user runs the application with `XAI_API_KEY` (or `OPENAI_API_KEY`) set in their terminal environment but not configured in their user secrets or `appsettings.json`, they expect live models to work.
2. **First Gap (Aspire AppHost)**: `DigitalBrainResource.SecretParam` detects that `Parameters:grok-api-key` is null in the Aspire Host configuration and eagerly overwrites it with `"placeholder"` to suppress interactive prompts. This completely ignores the terminal's `XAI_API_KEY` value. As a result, the Orleans Silo is started with the environment variable `DigitalBrain__Ai__GrokApiKey="placeholder"`.
3. **Second Gap (Grok Provider Factory)**: Since `config["DigitalBrain:Ai:GrokApiKey"]` is `"placeholder"`, `GrokProviderFactory.IsConfigured` returns `false` (as it only checks this config path and ignores `XAI_API_KEY`).
4. **Third Gap (Silo Service Registration)**: Because `IsConfigured` is `false`, `DigitalBrainAiBridge.ConfigureRealProviders` does **not** register the keyed `IChatClient` in the silo's dependency injection container.
5. **Impact on MCP**: If `IChatClient` is not registered, Orleans activations trying to resolve generic `Llm` neurons targeting Grok models throw DI resolution exceptions. Even direct `Grok` neuron activations, which bypass DI using `GrokConnector(apiKey, "grok-beta")`, will end up passing `"mock-xai-api-key"` (or throwing) because `XAI_API_KEY` was never forwarded from the host environment to the silo's environment. The MCP gateway `brain` tool invokes the Orleans kernel via gRPC, which triggers this broken dynamic neuron flow, breaking the live integration entirely.

---

## 3. Caveats
- Checked and traced all configuration pathways, but did not execute actual live API calls, as network access is restricted to read-only CODE_ONLY mode.
- Assumed standard Aspire parameter behavior where `WithEnvironment` references the resolved value of the `ParameterResource` at execution.
- Alignment with other providers: Handled `OPENAI_API_KEY` and `ANTHROPIC_API_KEY` in the exact same manner for parity and architectural soundness.

---

## 4. Conclusion
To make xAI (and other LLM providers) integrate seamlessly and robustly using standard environment variables like `XAI_API_KEY`:
1. The **Aspire AppHost** must check the host process environment variables (such as `XAI_API_KEY`) when suppressing cold boot prompts, and use them as parameters fallback values instead of hardcoding `"placeholder"`.
2. The **LLM Provider Factories** (`GrokProviderFactory.cs` and `OpenAiProviderFactory.cs`) must check both local configuration *and* environment variables (`XAI_API_KEY` / `OPENAI_API_KEY`) in `IsConfigured` and `CreateClient`.
3. The **Swarm Tests** must support checking `XAI_API_KEY` to successfully execute live integration checks.

---

## 5. Verification Method
1. **Inspect Code Files**:
   Verify the edits match the proposed diffs in:
   - `kernel/DigitalBrain.Hosting/DigitalBrain/DigitalBrainResource.cs`
   - `sdk/DigitalBrain.SDK/Ai/Llm/Providers/GrokProviderFactory.cs`
   - `sdk/DigitalBrain.SDK/Ai/Llm/Providers/OpenAiProviderFactory.cs`
   - `DigitalBrain.Test/Swarm/SwarmRealGrokTests.cs`
2. **Run Integration Tests**:
   - Execute the test suite using `dotnet test --filter SwarmRealGrokTests` in a terminal where `XAI_API_KEY` is exported.
   - Verify the test passes, demonstrating that `XAI_API_KEY` is correctly propagated, registered in the Silo's DI container, and used to communicate with live models.
3. **Invalidation Conditions**:
   - If `dotnet test --filter SwarmRealGrokTests` fails or throws `KeyedService` exceptions, it indicates that the keyed `IChatClient` is still not being registered in DI.
