using System.Text.Json;

namespace DigitalBrain.Microsoft.GitHub;

internal static class GitHubWebhookHandler
{
    internal static (int? Number, bool Revoke, GitHubWebhookAcceptance? Failure) Classify(GitHubRepositoryBinding binding, JsonElement payload, string eventName)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return (null, false, GitHubWebhookAcceptance.BadRequest);
        }

        if (eventName == "ping")
        {
            var appId = Number(payload, "hook", "app_id");
            return appId is null || appId == binding.AppId ? (null, false, null) : (null, false, GitHubWebhookAcceptance.Unauthorized);
        }

        if (Number(payload, "installation", "id") != binding.InstallationId)
        {
            return (null, false, GitHubWebhookAcceptance.Unauthorized);
        }

        var action = String(payload, "action");
        if (eventName == "installation" && action is "deleted" or "suspend")
        {
            return (null, true, null);
        }

        if (eventName == "installation_repositories")
        {
            var removed = payload.TryGetProperty("repositories_removed", out var repositories) && repositories.ValueKind == JsonValueKind.Array && repositories.EnumerateArray().Any(repository => Number(repository, "id") == binding.RepositoryId);
            return removed ? (null, true, null) : (null, false, GitHubWebhookAcceptance.Ignored);
        }

        if (Number(payload, "repository", "id") != binding.RepositoryId)
        {
            return (null, false, GitHubWebhookAcceptance.Unauthorized);
        }

        if (eventName == "repository" && action is "deleted" or "transferred" or "renamed" or "archived")
        {
            return (null, true, null);
        }

        if (!string.Equals(String(payload, "repository", "name"), binding.RepoName, StringComparison.OrdinalIgnoreCase) || !string.Equals(String(payload, "repository", "owner", "login"), binding.RepoOwner, StringComparison.OrdinalIgnoreCase))
        {
            // A trusted rename/transfer requires explicit reauthorization of the coordinates.
            return (null, true, null);
        }

        if (eventName == "pull_request")
        {
            if (action is not ("opened" or "reopened" or "synchronize" or "ready_for_review" or "converted_to_draft" or "closed" or "edited"))
            {
                return (null, false, GitHubWebhookAcceptance.Ignored);
            }

            var number = Number(payload, "number");
            return number is > 0 and <= int.MaxValue ? ((int)number.Value, false, null) : (null, false, GitHubWebhookAcceptance.BadRequest);
        }

        // Check payloads may have no PR association for forks. Reconcile authoritative open PRs.
        return eventName is "check_run" or "check_suite" or "status" ? (null, false, null) : (null, false, GitHubWebhookAcceptance.Ignored);
    }

    internal static string? String(JsonElement value, params string[] path) => TryProperty(value, path, out var found) && found.ValueKind == JsonValueKind.String ? found.GetString() : null;
    internal static long? Number(JsonElement value, params string[] path) => TryProperty(value, path, out var found) && found.ValueKind == JsonValueKind.Number && found.TryGetInt64(out var number) ? number : null;
    internal static bool TryProperty(JsonElement value, string[] path, out JsonElement found)
    {
        foreach (var part in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part, out value))
            {
                found = default;
                return false;
            }
        }

        found = value;
        return true;
    }
}
