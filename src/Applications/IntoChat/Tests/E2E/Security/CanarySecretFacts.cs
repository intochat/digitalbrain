using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Testing.E2E;
using IntoChat.Tests.E2E.Diagnostics;

namespace IntoChat.Tests.E2E.Security;

// A secret must not appear in HTTP responses, traces, or logs. SecretsFacts inspects persisted state.
public sealed class CanarySecretFacts
{
    private const string Owner = "owner";
    private const string Canary = "canary-secret-7f3a91";

    [Fact(Timeout = 300_000)]
    public async Task AnotherOwnersSecretIsForbiddenWhateverThePathSays()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.Create().StartAsync(ct);

        using var write = await brain.HttpClient.PostAsJsonAsync(
            "/secrets/someone-else",
            new { name = "me.apiKey", label = "API key", value = Canary },
            ct);

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact(Timeout = 300_000)]
    public async Task ASeededVaultSecretNeverAppearsInPlaintext()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var collector = TestTelemetryCollector.Start();
        await using var brain = await IntoChatE2ETest.Create()
            .WithResourceEnvironment(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = collector.Endpoint.AbsoluteUri,
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            })
            .StartAsync(ct);

        using var seeded = await brain.HttpClient.PostAsJsonAsync(
            $"/secrets/{Owner}",
            new { name = "me.apiKey", label = "API key", value = Canary },
            ct);
        Assert.Equal(System.Net.HttpStatusCode.OK, seeded.StatusCode);
        var seedBody = await seeded.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain(Canary, seedBody, StringComparison.Ordinal);
        Assert.Contains("secret://", seedBody, StringComparison.Ordinal);

        await WaitForAsync(() => collector.Snapshot().Count > 0, ct);
        await Task.Delay(TimeSpan.FromSeconds(3), ct);
        Assert.DoesNotContain(collector.Snapshot(), span => TextOf(span).Contains(Canary, StringComparison.Ordinal));
        Assert.DoesNotContain(collector.LogSnapshot(), log => TextOf(log).Contains(Canary, StringComparison.Ordinal));
        Assert.Empty(collector.Errors());

    }

    [Theory(Timeout = 300_000)]
    [InlineData("salesforce")]
    [InlineData("gmail")]
    [InlineData("webresearch")]
    public async Task AConnectionTokenNeverAppearsInPlaintext(string source)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var collector = TestTelemetryCollector.Start();
        await using var brain = await IntoChatE2ETest.Create()
            .WithResourceEnvironment(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = collector.Endpoint.AbsoluteUri,
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            })
            .StartAsync(ct);

        using var seeded = await brain.HttpClient.PostAsJsonAsync(
            $"/connections/{Owner}/connect",
            new { source, connectionId = $"conn-{source}", label = source, value = Canary },
            ct);
        Assert.Equal(System.Net.HttpStatusCode.OK, seeded.StatusCode);
        var seedBody = await seeded.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain(Canary, seedBody, StringComparison.Ordinal);
        Assert.Contains("secret://", seedBody, StringComparison.Ordinal);

        var list = await brain.HttpClient.GetStringAsync($"/connections/{Owner}", ct);
        Assert.DoesNotContain(Canary, list, StringComparison.Ordinal);

        await WaitForAsync(() => collector.Snapshot().Count > 0, ct);
        await Task.Delay(TimeSpan.FromSeconds(3), ct);
        Assert.DoesNotContain(collector.Snapshot(), span => TextOf(span).Contains(Canary, StringComparison.Ordinal));
        Assert.DoesNotContain(collector.LogSnapshot(), log => TextOf(log).Contains(Canary, StringComparison.Ordinal));
        Assert.Empty(collector.Errors());
    }

    [Fact(Timeout = 300_000)]
    public async Task AFormSecretTravelsOnlyAsAVaultReference()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var collector = TestTelemetryCollector.Start();
        await using var brain = await IntoChatE2ETest.Create()
            .WithResourceEnvironment(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = collector.Endpoint.AbsoluteUri,
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            })
            .StartAsync(ct);

        const string workspace = "form-canary";
        var scope = Scope(Owner, workspace);
        var form = brain.Get<DigitalBrain.Flutter.Form.IForm>(scope + "/apps/forms/intake");
        await form.Define(new("Intake", [new("password", "Password", DigitalBrain.Contracts.Types.FieldKind.Secret)]));

        using var seeded = await brain.HttpClient.PostAsJsonAsync(
            $"/secrets/{Owner}",
            new { name = "forms.intake.password", label = "Password", value = Canary },
            ct);
        Assert.Equal(System.Net.HttpStatusCode.OK, seeded.StatusCode);
        var reference = (await seeded.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("reference").GetString();
        Assert.NotNull(reference);
        Assert.StartsWith("secret://", reference, StringComparison.Ordinal);
        Assert.DoesNotContain(Canary, reference, StringComparison.Ordinal);

        using var evented = await brain.HttpClient.PostAsJsonAsync(
            $"/workspaces/{workspace}/apps/event",
            new { kind = "form", name = scope + "/apps/forms/intake", action = "secret", field = "password", value = reference },
            ct);
        Assert.Equal(System.Net.HttpStatusCode.OK, evented.StatusCode);

        var state = await form.Read();
        var field = state.Fields.Single();
        Assert.True(field.SecretSet);
        Assert.Null(field.Value);
        Assert.Equal(reference, field.Secret!.Reference);
        Assert.DoesNotContain(Canary, System.Text.Json.JsonSerializer.Serialize(state), StringComparison.Ordinal);

        await WaitForAsync(() => collector.Snapshot().Count > 0, ct);
        await Task.Delay(TimeSpan.FromSeconds(3), ct);
        Assert.DoesNotContain(collector.Snapshot(), span => TextOf(span).Contains(Canary, StringComparison.Ordinal));
        Assert.DoesNotContain(collector.LogSnapshot(), log => TextOf(log).Contains(Canary, StringComparison.Ordinal));
        Assert.Empty(collector.Errors());
    }

    [Fact(Timeout = 300_000)]
    public async Task BehaviorConsoleIsGatedByServerDeveloperMode()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var on = await IntoChatE2ETest.Create().StartAsync(ct);

        var capabilities = await on.HttpClient.GetFromJsonAsync<JsonElement>("/session/capabilities", ct);
        Assert.True(capabilities.GetProperty("developerMode").GetBoolean());

        await using var off = await IntoChatE2ETest.Create()
            .WithResourceEnvironment(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["IntoChat__DeveloperMode"] = "false",
            })
            .StartAsync(ct);

        var gated = await off.HttpClient.GetFromJsonAsync<JsonElement>("/session/capabilities", ct);
        Assert.False(gated.GetProperty("developerMode").GetBoolean());
        using var behaviors = await off.HttpClient.GetAsync($"/workspaces/{Owner}/behaviors/", ct);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, behaviors.StatusCode);
    }

    private static string Scope(string owner, string workspace)
    {
        var digest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(owner + "\0" + workspace));
        return "workspace-" + Convert.ToHexStringLower(digest);
    }

    private static string TextOf(CapturedSpan span)
        => string.Join("\n", span.Attributes.Select(pair => pair.Key + "=" + pair.Value));

    private static string TextOf(CapturedLog log)
        => log.Body + "\n" + string.Join("\n", log.Attributes.Select(pair => pair.Key + "=" + pair.Value));

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }
    }
}
