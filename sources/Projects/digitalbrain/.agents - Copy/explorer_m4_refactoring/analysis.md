# Comprehensive Analysis Report: LLM/xAI Integration & MCP Tool Gateway

This report provides a thorough read-only analysis of the LLM/xAI integration, environment variable credentials configurations, and the Model Context Protocol (MCP) tool gateway in DigitalBrain. It details file locations, current logic, gaps, and offers a step-by-step implementation blueprint and exact diff suggestions.

---

## 1. Code Base Paths & Current Logic

### A. Grok Neuron
- **File Path**: `sdk/DigitalBrain.SDK/Ai/Llm/Neuron/Grok.cs`
- **Current Logic**:
  - Inherits from `Llm` and targets grain type `"DigitalBrain.Ai.Grok"`.
  - In `OnActivateAsync`, it attempts to load `xai-api-key` from `ISecretVault` (a DPAPI-protected private secret vault).
  - If decryption fails or the returned key is empty, it catches the exception, logs a warning, and falls back to `Environment.GetEnvironmentVariable("XAI_API_KEY")`.
  - If that is also null or empty, it defaults to `"mock-xai-api-key"`.
  - It then constructs a `GrokConnector` client targeting `"grok-beta"`.
- **Gaps Identified**:
  - The grain itself is robust against private vault failures, but it relies on Orleans services. However, if the keyed service for Grok is not registered in DI on the Silo startup, general router neurons (like `Llm.cs` resolving dynamic scopes) will fail to resolve the Grok chat client, completely isolating the `Grok` grain itself.

### B. Grok Chat Client Factory
- **File Path**: `sdk/DigitalBrain.SDK/Ai/Llm/Providers/GrokProviderFactory.cs`
- **Current Logic**:
  - `IsConfigured(IConfiguration config)` returns true if `config["DigitalBrain:Ai:GrokApiKey"]` is not null or empty.
  - `CreateClient(LlmModel model, IConfiguration config)` reads `config["DigitalBrain:Ai:GrokApiKey"]`. If missing/placeholder, it falls back to resolving `ISecretVault` and calling `secretVault.DecryptSecret("grok-api-key")`.
  - If both are missing, it throws an `InvalidOperationException` advising secure setup using `set-private global:grok-api-key`.
- **Gaps Identified**:
  - `GrokProviderFactory` does **not** check the `XAI_API_KEY` environment variable.
  - If a user sets `XAI_API_KEY` in their shell environment, `IsConfigured` will return `false` if `DigitalBrain:Ai:GrokApiKey` is not configured in `appsettings.json` or Aspire configuration.
  - Consequently, the Silo builder (`DigitalBrainAiBridge.cs`) will skip registering the keyed `IChatClient` in the service container.
  - Furthermore, `CreateClient` does not check `XAI_API_KEY` environment variable as a fallback, causing it to throw an exception even if the environment variable is present.

### C. Silo DI Service Registration
- **File Path**: `sdk/DigitalBrain.SDK/Ai/DigitalBrainAiBridge.cs`
- **Current Logic**:
  - In `ConfigureRealProviders(IHostApplicationBuilder builder, IConfiguration configuration)`, it loops through `LlmModel.All`.
  - For each model, it checks if `factory.IsConfigured(configuration)` is true. If so, it registers the keyed chat client in the DI container:
    ```csharp
    builder.Services.AddKeyedSingleton<IChatClient>(model.ServiceKey, (sp, _) =>
        new ChatClientBuilder(factory.CreateClient(model, configuration))
            .UseLogging(sp.GetRequiredService<ILoggerFactory>())
            .Build());
    ```
- **Gaps Identified**:
  - Since `GrokProviderFactory.IsConfigured` returns `false` when `DigitalBrain:Ai:GrokApiKey` is missing (ignoring `XAI_API_KEY`), the keyed chat client service `IChatClient` is never registered. This makes it impossible for generic `Llm` neurons to resolve xAI Grok models dynamically.

### D. Aspire AppHost Configuration
- **File Paths**:
  - `kernel/DigitalBrain.Hosting/DigitalBrain/DigitalBrainResource.cs`
  - `kernel/DigitalBrain.Hosting/DigitalBrain/AiDomainBuilder.cs`
- **Current Logic**:
  - In `DigitalBrainResource.SecretParam(string parameterName, string description)`:
    - To suppress cold terminal prompt during start, if `Parameters:grok-api-key` is null, it sets it to `"placeholder"`.
    - It adds the parameter `grok-api-key` with Aspire `AppBuilder.AddParameter(parameterName, secret: true)`.
  - In `AiDomainBuilder.ApplyTo(IResourceBuilder<ProjectResource> silo)`:
    - If `grok-api-key` is present in `Secrets` (which it is, since `WithLlmProvider<Grok>()` is called), it injects:
      `silo.WithEnvironment("DigitalBrain__Ai__GrokApiKey", grok);`
- **Gaps Identified**:
  - Aspire AppHost ignores the host's terminal environment variable `XAI_API_KEY`.
  - When the user runs `digitalbrain.cs` with `XAI_API_KEY` exported in their shell, Aspire does not pass it down to the silo parameter because it gets overwritten by `"placeholder"`.
  - Thus, the silo's environment variable `DigitalBrain__Ai__GrokApiKey` remains `"placeholder"`, and the Grok provider is marked unconfigured in DI.

---

## 2. MCP Tool Gateway Implementation & Analysis

### A. Server Setup & Tool Binding
- **File Path**: `sdk/DigitalBrain.SDK.Mcp/DigitalBrain.SDK.Mcp/Program.cs`
- **Logic**:
  - Boots a WebApplication, importing ServiceDefaults (`AddServiceDefaults`).
  - Resolves gRPC channel address using `Environment.GetEnvironmentVariable("KERNEL_ENDPOINT")` (defaults to `https://localhost:7000`). It bypasses SSL certificate checks using `DangerousAcceptAnyServerCertificateValidator` to allow dev-cluster communication.
  - Registers the gRPC client types `DigitalBrainGatewayClient` and `BrainWatchClient` in the DI container.
  - Starts the stateless Model Context Protocol (MCP) server, mapping tool routes to `/mcp` and registering `BrainTools` class.

### B. Tool Resolution Flow
- **File Path**: `sdk/DigitalBrain.SDK.Mcp/DigitalBrain.SDK.Mcp/Tools/BrainTools.cs`
- **Logic**:
  - **Tool `brain`**:
    1. Generates a new Correlation ID.
    2. Opens a watch feed stream via `gateway.WatchHomeFeed`.
    3. Submits the prompt using `gateway.SubmitPromptAsync`.
    4. Enters a poll loop reading stream updates matching the Correlation ID.
    5. Detects terminal states (using `CardFold.IsTerminal`).
    6. Calls `CardFold.Reduce` to reduce the UI cards into a structured `BrainResult` containing generated `.feature` specification, source code, and verification test logs.
  - **Tool `list_neurons`**:
    1. Calls `brainWatch.SnapshotAsync`.
    2. Formats all active node models (ID, domain, timestamps) into a concise JSON array.

### C. Live Integration Mechanics & Secrets Flow
- The MCP server itself is stateless; it translates MCP tool calls directly into gRPC calls targeting the Orleans Silo.
- When `brain` is called, the request is dispatched to the core `GenesisNeuron` inside the Orleans silo.
- To execute, the kernel compiles and evaluates dynamic neurons. If these neurons reference Grok (e.g. `using grok = neuron(DigitalBrain.Ai.Grok["xai-grok-beta"])`), Orleans activates the `Grok` neuron grain.
- If `XAI_API_KEY` is not set or is `"placeholder"`, the live call fails, meaning MCP prompts requiring live Grok capability cannot build or run.
- Therefore, successfully passing the host's terminal `XAI_API_KEY` into the Aspire parameters and injecting it into the silo environment is crucial for full MCP live functionality.

---

## 3. Step-by-Step Integration & Refactoring Blueprint

### Step 1: Aspire Host Configuration Environment Loading
Update `DigitalBrainResource.SecretParam` in `kernel/DigitalBrain.Hosting/DigitalBrain/DigitalBrainResource.cs` to check the host process environment when a parameter is null in configuration.
- If the configuration is missing `Parameters:<param>`, it should extract the matching environment variable (e.g., `XAI_API_KEY` for `grok-api-key`, `OPENAI_API_KEY` for `openai-api-key`, and `ANTHROPIC_API_KEY` for `anthropic-api-key`).
- This binds live keys to Aspire parameters at start, ensuring automatic forwarding to silos.

### Step 2: Update Grok Provider Factory to Support Environment Fallback
Update `sdk/DigitalBrain.SDK/Ai/Llm/Providers/GrokProviderFactory.cs`:
- Modify `IsConfigured` to return true if the key is present in configuration OR `XAI_API_KEY` is set in the environment.
- Modify `CreateClient` to check configuration, and if empty/placeholder, check `Environment.GetEnvironmentVariable("XAI_API_KEY")`. Fall back to `ISecretVault` if both are missing.

### Step 3: Update OpenAI Provider Factory to Support Environment Fallback
Update `sdk/DigitalBrain.SDK/Ai/Llm/Providers/OpenAiProviderFactory.cs` in an identical manner:
- Check both configuration `DigitalBrain:Ai:OpenAiApiKey` and environment `OPENAI_API_KEY` in `IsConfigured` and `CreateClient`.

### Step 4: Update Swarm Tests to Support Environment Fallback
Update `DigitalBrain.Test/Swarm/SwarmRealGrokTests.cs`:
- Support loading API keys from `XAI_API_KEY` directly, allowing live integration tests to execute cleanly on CI/CD environments and local terminals where only `XAI_API_KEY` is exported.

---

## 4. Proposed File Diffs

### Diff A: `kernel/DigitalBrain.Hosting/DigitalBrain/DigitalBrainResource.cs`
```csharp
<<<< [Line 83 - 93]
        // Suppress terminal prompting on cold boot if key is not configured in local environment/secrets
        if (AppBuilder.Configuration[$"Parameters:{parameterName}"] is null)
        {
            AppBuilder.Configuration[$"Parameters:{parameterName}"] = "placeholder";
        }

        var parameter = AppBuilder.AddParameter(parameterName, secret: true)
            .WithDescription(description, enableMarkdown: true);
        Secrets[parameterName] = parameter;
        return parameter;
====
        // Suppress terminal prompting on cold boot if key is not configured in local environment/secrets
        if (AppBuilder.Configuration[$"Parameters:{parameterName}"] is null)
        {
            string? envFallback = null;
            if (parameterName == "grok-api-key")
            {
                envFallback = Environment.GetEnvironmentVariable("XAI_API_KEY") 
                    ?? Environment.GetEnvironmentVariable("DigitalBrain__Ai__GrokApiKey")
                    ?? Environment.GetEnvironmentVariable("grok-api-key");
            }
            else if (parameterName == "openai-api-key")
            {
                envFallback = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    ?? Environment.GetEnvironmentVariable("DigitalBrain__Ai__OpenAiApiKey")
                    ?? Environment.GetEnvironmentVariable("openai-api-key");
            }
            else if (parameterName == "anthropic-api-key")
            {
                envFallback = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                    ?? Environment.GetEnvironmentVariable("DigitalBrain__Ai__AnthropicApiKey")
                    ?? Environment.GetEnvironmentVariable("anthropic-api-key");
            }

            AppBuilder.Configuration[$"Parameters:{parameterName}"] = envFallback ?? "placeholder";
        }

        var parameter = AppBuilder.AddParameter(parameterName, secret: true)
            .WithDescription(description, enableMarkdown: true);
        Secrets[parameterName] = parameter;
        return parameter;
>>>>
```

### Diff B: `sdk/DigitalBrain.SDK/Ai/Llm/Providers/GrokProviderFactory.cs`
```csharp
<<<< [Line 11 - 44]
    public bool IsConfigured(IConfiguration config)
        => !string.IsNullOrEmpty(config["DigitalBrain:Ai:GrokApiKey"]);

    public IChatClient CreateClient(LlmModel model, IConfiguration config)
    {
        var apiKey = config["DigitalBrain:Ai:GrokApiKey"];
        
        if (string.IsNullOrEmpty(apiKey) || apiKey == "placeholder")
        {
            if (SdkRuntime.ServiceProvider is { } sp)
            {
                var secretVault = sp.GetService(typeof(DigitalBrain.SDK.Security.ISecretVault)) as DigitalBrain.SDK.Security.ISecretVault;
                if (secretVault is not null)
                {
                    try
                    {
                        var decrypted = secretVault.DecryptSecret("grok-api-key");
                        if (!string.IsNullOrEmpty(decrypted) && decrypted != "placeholder")
                        {
                            apiKey = decrypted;
                        }
                    }
                    catch (System.Collections.Generic.KeyNotFoundException) { }
                    catch (System.Exception) { }
                }
            }
        }

        if (string.IsNullOrEmpty(apiKey) || apiKey == "placeholder")
            throw new InvalidOperationException(
                $"Grok API key is required for provider 'grok' (model '{model.Id}'), but is not set. " +
                "Please configure it securely by entering standard command: 'set-private global:grok-api-key=<your-key>' in the visual client prompt.");

        return new GrokConnector(apiKey, model.Id);
====
    public bool IsConfigured(IConfiguration config)
        => !string.IsNullOrEmpty(config["DigitalBrain:Ai:GrokApiKey"]) ||
           !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XAI_API_KEY"));

    public IChatClient CreateClient(LlmModel model, IConfiguration config)
    {
        var apiKey = config["DigitalBrain:Ai:GrokApiKey"];
        
        if (string.IsNullOrEmpty(apiKey) || apiKey == "placeholder")
        {
            apiKey = Environment.GetEnvironmentVariable("XAI_API_KEY");
        }

        if (string.IsNullOrEmpty(apiKey) || apiKey == "placeholder")
        {
            if (SdkRuntime.ServiceProvider is { } sp)
            {
                var secretVault = sp.GetService(typeof(DigitalBrain.SDK.Security.ISecretVault)) as DigitalBrain.SDK.Security.ISecretVault;
                if (secretVault is not null)
                {
                    try
                    {
                        var decrypted = secretVault.DecryptSecret("grok-api-key");
                        if (!string.IsNullOrEmpty(decrypted) && decrypted != "placeholder")
                        {
                            apiKey = decrypted;
                        }
                    }
                    catch (System.Collections.Generic.KeyNotFoundException) { }
                    catch (System.Exception) { }
                }
            }
        }

        if (string.IsNullOrEmpty(apiKey) || apiKey == "placeholder")
            throw new InvalidOperationException(
                $"Grok API key is required for provider 'grok' (model '{model.Id}'), but is not set. " +
                "Please configure it securely by entering standard command: 'set-private global:grok-api-key=<your-key>' in the visual client prompt.");

        return new GrokConnector(apiKey, model.Id);
>>>>
```

### Diff C: `sdk/DigitalBrain.SDK/Ai/Llm/Providers/OpenAiProviderFactory.cs`
```csharp
<<<< [Line 12 - 47]
    public bool IsConfigured(IConfiguration config)
        => !string.IsNullOrEmpty(config["DigitalBrain:Ai:OpenAiApiKey"]);

    public IChatClient CreateClient(LlmModel model, IConfiguration config)
    {
        var apiKey = config["DigitalBrain:Ai:OpenAiApiKey"];
        
        if (string.IsNullOrEmpty(apiKey) || apiKey == "placeholder")
        {
            if (SdkRuntime.ServiceProvider is { } sp)
            {
                var secretVault = sp.GetService(typeof(DigitalBrain.SDK.Security.ISecretVault)) as DigitalBrain.SDK.Security.ISecretVault;
                if (secretVault is not null)
                {
                    try
                    {
                        var decrypted = secretVault.DecryptSecret("openai-api-key");
                        if (!string.IsNullOrEmpty(decrypted) && decrypted != "placeholder")
                        {
                            apiKey = decrypted;
                        }
                    }
                    catch (System.Collections.Generic.KeyNotFoundException) { }
                    catch (System.Exception) { }
                }
            }
        }

        if (string.IsNullOrEmpty(apiKey) || apiKey == "placeholder")
            throw new InvalidOperationException(
                $"OpenAI API key is required for provider 'openai' (model '{model.Id}'), but is not set. " +
                "Please configure it securely by entering standard command: 'set-private global:openai-api-key=<your-key>' in the visual client prompt.");

        var clientOptions = new OpenAIClientOptions();
        var openAi = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        return openAi.GetChatClient(model.Id).AsIChatClient();
====
    public bool IsConfigured(IConfiguration config)
        => !string.IsNullOrEmpty(config["DigitalBrain:Ai:OpenAiApiKey"]) ||
           !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

    public IChatClient CreateClient(LlmModel model, IConfiguration config)
    {
        var apiKey = config["DigitalBrain:Ai:OpenAiApiKey"];
        
        if (string.IsNullOrEmpty(apiKey) || apiKey == "placeholder")
        {
            apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        }

        if (string.IsNullOrEmpty(apiKey) || apiKey == "placeholder")
        {
            if (SdkRuntime.ServiceProvider is { } sp)
            {
                var secretVault = sp.GetService(typeof(DigitalBrain.SDK.Security.ISecretVault)) as DigitalBrain.SDK.Security.ISecretVault;
                if (secretVault is not null)
                {
                    try
                    {
                        var decrypted = secretVault.DecryptSecret("openai-api-key");
                        if (!string.IsNullOrEmpty(decrypted) && decrypted != "placeholder")
                        {
                            apiKey = decrypted;
                        }
                    }
                    catch (System.Collections.Generic.KeyNotFoundException) { }
                    catch (System.Exception) { }
                }
            }
        }

        if (string.IsNullOrEmpty(apiKey) || apiKey == "placeholder")
            throw new InvalidOperationException(
                $"OpenAI API key is required for provider 'openai' (model '{model.Id}'), but is not set. " +
                "Please configure it securely by entering standard command: 'set-private global:openai-api-key=<your-key>' in the visual client prompt.");

        var clientOptions = new OpenAIClientOptions();
        var openAi = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        return openAi.GetChatClient(model.Id).AsIChatClient();
>>>>
```

### Diff D: `DigitalBrain.Test/Swarm/SwarmRealGrokTests.cs`
```csharp
<<<< [Line 36]
            var apiKey = Environment.GetEnvironmentVariable("DigitalBrain__Ai__GrokApiKey") ?? ApiKey;
====
            var apiKey = Environment.GetEnvironmentVariable("DigitalBrain__Ai__GrokApiKey") 
                ?? Environment.GetEnvironmentVariable("XAI_API_KEY") 
                ?? ApiKey;
>>>>

<<<< [Line 68 - 73]
        var apiKey = Environment.GetEnvironmentVariable("DigitalBrain__Ai__GrokApiKey");
        if (string.IsNullOrEmpty(apiKey))
        {
            // Skip the real Grok integration test when no API key is set in environment
            return;
        }
====
        var apiKey = Environment.GetEnvironmentVariable("DigitalBrain__Ai__GrokApiKey")
            ?? Environment.GetEnvironmentVariable("XAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            // Skip the real Grok integration test when no API key is set in environment
            return;
        }
>>>>
```
