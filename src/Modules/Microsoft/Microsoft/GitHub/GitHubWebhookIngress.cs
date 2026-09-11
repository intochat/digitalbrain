using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Microsoft.GitHub;

internal sealed class GitHubWebhookIngress(GitHubRepositoryBindings bindings, IGrainFactory grains)
{
    public async Task<GitHubWebhookAcceptance> HandleAsync(ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, string[]> headers, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var delivery = Header(headers, "X-GitHub-Delivery");
        var eventName = Header(headers, "X-GitHub-Event");
        if (!Guid.TryParse(delivery, out var deliveryId) || string.IsNullOrWhiteSpace(eventName))
        {
            return GitHubWebhookAcceptance.BadRequest;
        }
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 });
            var payload = document.RootElement;
            if (payload.ValueKind != JsonValueKind.Object)
            {
                return GitHubWebhookAcceptance.BadRequest;
            }
            var installation = RoutingHint(payload, "installation", "id");
            var repository = RoutingHint(payload, "repository", "id");
            var app = RoutingHint(payload, "hook", "app_id");
            var signature = Header(headers, "X-Hub-Signature-256");
            var ping = eventName == "ping";
            var targets = bindings.All.Where(binding => installation is not null
                ? binding.InstallationId == installation && (repository is null || binding.RepositoryId == repository)
                : app is not null && binding.AppId == app).ToArray();
            if (ping && installation is null && app is null)
            {
                targets = bindings.All.Where(binding => ValidateSignature(body.Span, signature, binding.WebhookSecret)).ToArray();
            }
            if (targets.Length == 0)
            {
                return ping ? GitHubWebhookAcceptance.Unauthorized : GitHubWebhookAcceptance.Ignored;
            }
            var results = new List<GitHubWebhookAcceptance>();
            foreach (var binding in targets)
            {
                if (!ValidateSignature(body.Span, signature, binding.WebhookSecret))
                {
                    results.Add(GitHubWebhookAcceptance.Unauthorized);
                    continue;
                }
                var classification = GitHubWebhookHandler.Classify(binding, payload, eventName);
                if (classification.Failure is { } failure)
                {
                    results.Add(failure);
                    continue;
                }
                // Refresh no longer targets a single SHA because the signal carries only the body hash.
                if (RejectCheckPayloadWithInvalidHeadSha(payload, eventName))
                {
                    results.Add(GitHubWebhookAcceptance.BadRequest);
                    continue;
                }
                using var activity = GitHubTelemetry.Source.StartActivity("github.webhook.accept", ActivityKind.Producer);
                GitHubTelemetry.TagReceipt(activity, binding, delivery!, classification.Number);
                var fact = new RepositoryEvent(delivery!, eventName,
                    classification.Revoke ? "revoked" : GitHubWebhookHandler.String(payload, "action"),
                    classification.Number, Convert.ToHexStringLower(SHA256.HashData(body.Span)));
                var signal = Signal.Create(GitHubSignals.RepositoryEvent, JsonSerializer.Serialize(fact, GitHubJson.Default.RepositoryEvent));
                try
                {
                    var accepted = await grains.GetGrain<INeuron>(new NeuronId("repository", binding.Id).ToGrainId())
                        .Deliver(new SignalDelivery(signal, new SignalId(deliveryId), CorrelationId.New(), null,
                            new NeuronId("github", "webhook"), 1, TimeProvider.System.GetUtcNow()), cancellationToken).ConfigureAwait(false);
                    results.Add(accepted switch
                    {
                        DeliveryAdmission.Accepted => GitHubWebhookAcceptance.Accepted,
                        DeliveryAdmission.Duplicate => GitHubWebhookAcceptance.Duplicate,
                        _ => GitHubWebhookAcceptance.Unavailable,
                    });
                }
                catch (Exception error)
                {
                    GitHubTelemetry.Failed(activity, error);
                    results.Add(GitHubWebhookAcceptance.Unavailable);
                }
            }
            // Acknowledge only when every intended target durably accepted its delivery.
            foreach (var failure in new[] { GitHubWebhookAcceptance.Unavailable, GitHubWebhookAcceptance.Unauthorized, GitHubWebhookAcceptance.BadRequest })
            {
                if (results.Contains(failure))
                {
                    return failure;
                }
            }
            return results.Contains(GitHubWebhookAcceptance.Accepted) ? GitHubWebhookAcceptance.Accepted
                : results.Contains(GitHubWebhookAcceptance.Duplicate) ? GitHubWebhookAcceptance.Duplicate : GitHubWebhookAcceptance.Ignored;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return GitHubWebhookAcceptance.BadRequest;
        }
    }

    private static bool RejectCheckPayloadWithInvalidHeadSha(JsonElement payload, string eventName)
    {
        var sha = eventName switch
        {
            "check_run" => GitHubWebhookHandler.String(payload, "check_run", "head_sha"),
            "check_suite" => GitHubWebhookHandler.String(payload, "check_suite", "head_sha"),
            "status" => GitHubWebhookHandler.String(payload, "sha"),
            _ => null,
        };
        return sha is not null && !GitHubRepositorySource.IsSha(sha);
    }

    private static long? RoutingHint(JsonElement payload, string parent, string name)
        => payload.TryGetProperty(parent, out var value) && value.TryGetProperty(name, out var number) ? number.GetInt64() : null;

    private static bool ValidateSignature(ReadOnlySpan<byte> body, string? signature, string secret)
    {
        if (signature is not { Length: 71 } || !signature.StartsWith("sha256=", StringComparison.Ordinal))
        {
            return false;
        }
        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(signature.AsSpan(7));
        }
        catch (FormatException)
        {
            return false;
        }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private static string? Header(IReadOnlyDictionary<string, string[]> headers, string name)
    {
        var matches = headers.Where(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 && matches[0].Value.Length == 1 ? matches[0].Value[0] : null;
    }
}
