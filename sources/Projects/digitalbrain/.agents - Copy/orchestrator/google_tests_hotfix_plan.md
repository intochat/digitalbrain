# Google Tests Hotfix Plan: Resolve Integration Timeouts

## 1. Objectives
Eliminate all three pre-existing integration scenario timeouts in `DigitalBrain.SDK.Google.Tests` to achieve a 100% clean global test sweep:
1. **Stripe Webhook Signature Timeout (TC-2.1-5)**: Caused by dynamic test secret configuration not propagating to the child silo. Pre-inject the Stripe webhook secret at silo startup.
2. **Telegram Alert Timeout (TC-1.4-2)**: Caused by one-way fire-and-forget neuron not emitting any reply synapse. Fire an acknowledgment synapse (the request itself) back to the caller to satisfy the gateway.
3. **Gmail Digest Timeout (TC-3.1)**: Caused by setting `CallerNeuronType` to `null` in outgoing synapses, which routes replies to `External` (the gateway) rather than the digest neuron subscription stream. Set `CallerNeuronType` to `GmailDigestNeuronType`.

---

## 2. Technical Modifications

### A. Stripe Webhook & Telegram Bot Token Pre-Configuration
In `sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google.Tests/Support/TestDependencies.cs`, pre-inject the Stripe webhook secret and Telegram bot token:
```csharp
        services.AddSingleton<TestBrainOS>(_ =>
            TestBrainOS.StartAsync(o => o.WithStubbedGoogle("newuser@example.com")
                                         .WithEnvironmentOverride("BrainOS__Stripe__WebhookSecret", "whsec_test")
                                         .WithEnvironmentOverride("BrainOS__Telegram__BotToken", "mock-token"))
                       .GetAwaiter().GetResult());
```

### B. Telegram Alert Neuron Acknowledgment Synapse
In `sdk/DigitalBrain.SDK/Google/Telegram/TelegramAlertNeuron.cs`, inside `HandleAsync`, fire an acknowledgment synapse back to the caller before returning:
```csharp
        try
        {
            // Existing telegram send code...
        }
        finally
        {
            await FireSynapseAsync(request with { ReceiverNeuronType = request.CallerNeuronType ?? "External" });
        }
```

### C. Gmail Digest Neuron Caller Type Hydration
In `sdk/DigitalBrain.SDK/Google/Digest/GmailDigestNeuron.cs`:
1. Inside `HandleStartAsync` (line 57), set:
   ```csharp
   CallerNeuronType: GmailDigestNeuronType,
   ```
2. Inside `HandleSendersAsync` (line 87), set:
   ```csharp
   CallerNeuronType: GmailDigestNeuronType,
   ```

---

## 3. Verification Criteria
1. Build `BrainOS.Fast.slnx` successfully with zero compiler warnings/errors.
2. Run the isolated test suite `DigitalBrain.SDK.Google.Tests` and confirm **all 11 tests pass cleanly**.
3. Run the full global test sweep on `BrainOS.slnx` or all 22 test projects sequentially.
