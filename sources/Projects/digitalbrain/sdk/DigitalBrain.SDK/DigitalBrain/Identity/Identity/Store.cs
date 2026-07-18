using DigitalBrain.Runtime.Runtime;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.Identity.Identity;

[GrainType("DigitalBrain.SDK.Identity.IdentityStore")]
public sealed class Store : DurableGrain, ICallNeuronTarget, IPredicateNeuronTarget
{
    private readonly IdentityStoreEngine _engine;

    public Store(
        [FromKeyedServices("ident-failed")] IDurableDictionary<string, int> failedAttempts,
        [FromKeyedServices("ident-lockouts")] IDurableDictionary<string, long> lockoutEnds,
        [FromKeyedServices("ident-tokens")] IDurableDictionary<string, string> encryptedTokens)
    {
        _engine = new IdentityStoreEngine(failedAttempts, lockoutEnds, encryptedTokens, async () => await WriteStateAsync());
    }

    public Task<string> AskAsync(string prompt) => _engine.AskAsync(prompt);

    public Task<bool> EvaluateAsync(string subject, string target, CancellationToken ct) => _engine.EvaluateAsync(subject, target, ct);
}

public sealed class IdentityStoreEngine(
    IDurableDictionary<string, int> failedAttempts,
    IDurableDictionary<string, long> lockoutEnds,
    IDurableDictionary<string, string> encryptedTokens,
    Func<Task> writeStateAsync)
{
    public async Task<string> AskAsync(string prompt)
    {
        if (prompt.StartsWith("validate-token ", StringComparison.Ordinal))
        {
            var token = prompt["validate-token ".Length..].Trim();
            foreach (var username in encryptedTokens.Keys)
            {
                if (encryptedTokens.TryGetValue(username, out var encryptedToken))
                {
                    try
                    {
                        if (AesEncryption.Decrypt(encryptedToken) == token)
                        {
                            return $"valid:{username}";
                        }
                    }
                    catch
                    {
                        // Ignore decryption failures for older or invalid entries
                    }
                }
            }
            return "invalid";
        }

        if (prompt.StartsWith("get-token ", StringComparison.Ordinal))
        {
            var username = prompt["get-token ".Length..].Trim();
            if (encryptedTokens.TryGetValue(username, out var encryptedToken))
            {
                try
                {
                    return AesEncryption.Decrypt(encryptedToken);
                }
                catch
                {
                    return "";
                }
            }
            return "";
        }

        if (prompt.StartsWith("login-card ", StringComparison.Ordinal))
        {
            var username = prompt["login-card ".Length..].Trim();
            return IdentityPlan.LoginCardDataJson(username);
        }

        if (prompt.StartsWith("reset-lockout ", StringComparison.Ordinal))
        {
            var username = prompt["reset-lockout ".Length..].Trim();
            failedAttempts.Remove(username);
            lockoutEnds.Remove(username);
            encryptedTokens.Remove(username);
            await writeStateAsync();
            return "ok";
        }

        if (prompt.StartsWith("logout ", StringComparison.Ordinal))
        {
            var username = prompt["logout ".Length..].Trim();
            encryptedTokens.Remove(username);
            await writeStateAsync();
            return "ok";
        }

        if (prompt.StartsWith("spawn-brain ", StringComparison.Ordinal))
        {
            var data = prompt["spawn-brain ".Length..].Trim();
            var parts = data.Split(':');
            if (parts.Length < 2)
            {
                return "error:invalid prompt format";
            }
            var userId = parts[0];
            var newBrainId = parts[1];
            var sourceBrainId = parts.Length > 2 && !string.IsNullOrEmpty(parts[2]) ? parts[2] : null;
            var syncTarget = parts.Length > 3 && !string.IsNullOrEmpty(parts[3]) ? parts[3] : "local";

            var regex = new System.Text.RegularExpressions.Regex(@"^[a-z0-9][a-z0-9-]{0,63}$");
            if (!regex.IsMatch(newBrainId))
            {
                return "error:invalid brain name format (must be 1-64 chars of a-z, 0-9, and hyphens, starting with alpha/numeric)";
            }

            var dbDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DigitalBrain", "databases");
            Directory.CreateDirectory(dbDir);
            
            var targetDbPath = Path.Combine(dbDir, $"{newBrainId}.db");
            
            if (!string.IsNullOrEmpty(sourceBrainId))
            {
                var sourceDbPath = Path.Combine(dbDir, $"{sourceBrainId}.db");
                if (File.Exists(sourceDbPath))
                {
                    try
                    {
                        File.Copy(sourceDbPath, targetDbPath, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        return $"error:failed to clone database: {ex.Message}";
                    }
                }
                else
                {
                    return $"error:source database '{sourceBrainId}' not found";
                }
            }
            else
            {
                try
                {
                    File.WriteAllBytes(targetDbPath, Array.Empty<byte>());
                }
                catch (Exception ex)
                {
                    return $"error:failed to create new database file: {ex.Message}";
                }
            }

            var plainToken = $"session-{userId}-{Guid.NewGuid()}";
            var encryptedToken = AesEncryption.Encrypt(plainToken);
            encryptedTokens[userId] = encryptedToken;
            await writeStateAsync();

            return $"success:{plainToken}";
        }

        return "";
    }

    public async Task<bool> EvaluateAsync(string subject, string target, CancellationToken ct)
    {
        if (subject.Contains(':'))
        {
            // This is "is-valid-login" evaluation!
            var parts = subject.Split(':', 2);
            var username = parts[0];
            var password = parts[1];

            // 1. Check if locked out
            bool isLocked = false;
            if (lockoutEnds.TryGetValue(username, out var endTicks))
            {
                var end = new DateTimeOffset(endTicks, TimeSpan.Zero);
                if (end > DateTimeOffset.UtcNow)
                {
                    isLocked = true;
                }
            }

            if (isLocked)
            {
                return string.Equals(target, "false", StringComparison.OrdinalIgnoreCase);
            }

            // 2. Validate credentials
            bool isValid = (username == "admin" && password == "admin123") || 
                           (username == "local" && password == "password") || 
                           (username == "user" && password == "user123");

            if (isValid)
            {
                // Reset failed attempts
                failedAttempts.Remove(username);
                lockoutEnds.Remove(username);

                // Generate session token and encrypt it at rest!
                var plainToken = $"session-{username}-{Guid.NewGuid()}";
                var encryptedToken = AesEncryption.Encrypt(plainToken);
                encryptedTokens[username] = encryptedToken;

                await writeStateAsync();

                return string.Equals(target, "true", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                // Increment failed attempts
                var currentAttempts = failedAttempts.TryGetValue(username, out var val) ? val : 0;
                currentAttempts++;
                failedAttempts[username] = currentAttempts;

                if (currentAttempts >= 3)
                {
                    // Lock out for 30 seconds
                    var lockoutEnd = DateTimeOffset.UtcNow.AddSeconds(30);
                    lockoutEnds[username] = lockoutEnd.Ticks;
                }

                await writeStateAsync();

                if (string.Equals(target, "false", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(target, "invalid", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            }
        }
        else
        {
            // This is "is-locked-out" evaluation!
            var username = subject;
            bool isLocked = false;

            if (lockoutEnds.TryGetValue(username, out var endTicks))
            {
                var end = new DateTimeOffset(endTicks, TimeSpan.Zero);
                if (end > DateTimeOffset.UtcNow)
                {
                    isLocked = true;
                }
            }

            bool expected = string.Equals(target, "true", StringComparison.OrdinalIgnoreCase);
            return isLocked == expected;
        }
    }
}
