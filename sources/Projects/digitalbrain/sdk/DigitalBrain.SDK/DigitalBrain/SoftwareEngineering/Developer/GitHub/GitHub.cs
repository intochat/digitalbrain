using System.Text;
using DigitalBrain.Core;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer.GitHub;

[GrainType(Fqn)]
[ImplicitStreamSubscription(nameof(GitHub))]
internal sealed class GitHub(ITokenProtector tokenProtector) 
    : Neuron(),
      IGitHub,
      ICallNeuronTarget,
      IHandle<GitHubAuthRequest>,
      IHandle<GitCommitRequest>,
      IHandle<SubmitPullRequest>,
      IHandle<GitStatusRequest>
{
    public const string Fqn = "DigitalBrain.Developer.GitHub";

    private string ProjectKey => this.GetPrimaryKeyString();

    private string GetWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "DigitalBrain.slnx")) || 
                System.IO.Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return @"e:\digitalbrain";
    }

    private async Task<string?> GetDecryptedTokenAsync()
    {
        var store = Grains.GetGrain<IGitHubCredentialStore>(ProjectKey);
        var encrypted = await store.GetEncryptedTokenAsync();
        if (encrypted == null || encrypted.Length == 0) return null;

        try
        {
            var bytes = tokenProtector.Unprotect(encrypted);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to decrypt GitHub token for {Project}", ProjectKey);
            return null;
        }
    }

    private async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(
        string command, string arguments, IDictionary<string, string>? env = null)
    {
        var workingDir = GetWorkspaceRoot();
        Logger.LogInformation("Executing process: {Cmd} {Args} in {Dir}", command, arguments, workingDir);

        using var process = new System.Diagnostics.Process();
        process.StartInfo.FileName = command;
        process.StartInfo.Arguments = arguments;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.WorkingDirectory = workingDir;

        if (env != null)
        {
            foreach (var kv in env)
            {
                process.StartInfo.Environment[kv.Key] = kv.Value;
            }
        }

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();
            return (process.ExitCode, outputBuilder.ToString().Trim(), errorBuilder.ToString().Trim());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Process execution failed: {Cmd}", command);
            return (-1, "", ex.Message);
        }
    }

    // --- IGitHub ---

    public async Task<GitStatusResponse> GetStatusAsync()
    {
        var (exitCode, output, error) = await RunProcessAsync("git", "status --porcelain");
        var branchResult = await RunProcessAsync("git", "branch --show-current");

        var responseHeaders = new SynapseMetadata(
            SynapseId: SynapseId.New(),
            CorrelationId: global::DigitalBrain.Runtime.Neurons.CorrelationId.New(),
            CausationId: null,
            CallerNeuronId: new NeuronId(InstanceId.ToString()),
            CallerNeuronType: NeuronType,
            ReceiverNeuronId: new NeuronId(Guid.Empty.ToString()),
            ReceiverNeuronType: "External",
            Timestamp: DateTimeOffset.UtcNow
        );

        if (exitCode != 0)
        {
            return new GitStatusResponse(Success: false,
                CurrentBranch: "unknown",
                ChangedFiles: Array.Empty<string>(),
                ErrorMessage: error) { Headers = responseHeaders };
        }

        var changedFiles = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Substring(3).Trim())
            .ToList();

        var branch = branchResult.Output.Trim();
        if (string.IsNullOrEmpty(branch)) branch = "master";

        return new GitStatusResponse(Success: true,
            CurrentBranch: branch,
            ChangedFiles: changedFiles) { Headers = responseHeaders };
    }

    public async Task<bool> CommitAsync(string message, IReadOnlyList<string>? files = null, bool autoStage = true)
    {
        if (autoStage)
        {
            if (files != null && files.Count > 0)
            {
                foreach (var file in files)
                {
                    var (stageCode, _, _) = await RunProcessAsync("git", $"add \"{file}\"");
                    if (stageCode != 0) return false;
                }
            }
            else
            {
                var (stageCode, _, _) = await RunProcessAsync("git", "add .");
                if (stageCode != 0) return false;
            }
        }

        var (commitCode, _, _) = await RunProcessAsync("git", $"commit -m \"{message}\"");
        return commitCode == 0;
    }

    public async Task<bool> SubmitPullRequestAsync(string title, string body, string sourceBranch, string targetBranch = "master", bool draft = false)
    {
        var token = await GetDecryptedTokenAsync();
        var env = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(token))
        {
            env["GITHUB_TOKEN"] = token;
        }

        var args = $"pr create --title \"{title}\" --body \"{body}\" --head \"{sourceBranch}\" --base \"{targetBranch}\"";
        if (draft) args += " --draft";

        var (prCode, _, _) = await RunProcessAsync("gh", args, env);
        return prCode == 0;
    }

    // --- ICallNeuronTarget ($ sigil) ---

    public async Task<string> AskAsync(string prompt)
    {
        if (string.IsNullOrEmpty(prompt)) return "Invalid empty prompt";

        var parts = prompt.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "status":
                var status = await GetStatusAsync();
                if (!status.Success) return $"Error: {status.ErrorMessage}";
                return $"Branch: {status.CurrentBranch}, Changed files: {string.Join(", ", status.ChangedFiles)}";

            case "commit":
                if (parts.Length < 2) return "Usage: commit <message>";
                var success = await CommitAsync(parts[1]);
                return success ? "ok" : "Failed to commit changes";

            case "pr":
                // Expected format: pr title=... body=... head=... base=... [draft=true]
                var title = ExtractArg(prompt, "title");
                var body = ExtractArg(prompt, "body");
                var head = ExtractArg(prompt, "head");
                var baseBr = ExtractArg(prompt, "base") ?? "master";
                var isDraft = string.Equals(ExtractArg(prompt, "draft"), "true", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(body) || string.IsNullOrEmpty(head))
                {
                    return "Usage error. Required parameters: title, body, head.";
                }

                var prSuccess = await SubmitPullRequestAsync(title, body, head, baseBr, isDraft);
                return prSuccess ? "Pull Request submitted successfully" : "Failed to submit Pull Request via gh CLI";

            default:
                return $"Unknown command: {cmd}";
        }
    }

    private static string? ExtractArg(string prompt, string key)
    {
        var pattern = $"{key}=";
        var idx = prompt.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (idx == -1) return null;

        var start = idx + pattern.Length;
        if (start >= prompt.Length) return "";

        // If quoted, read up to next quote
        if (prompt[start] == '"')
        {
            var end = prompt.IndexOf('"', start + 1);
            return end == -1 ? prompt.Substring(start + 1) : prompt.Substring(start + 1, end - start - 1);
        }
        else
        {
            var end = prompt.IndexOf(' ', start);
            return end == -1 ? prompt.Substring(start) : prompt.Substring(start, end - start);
        }
    }

    // --- Synapse Handlers ---

    public async Task HandleAsync(GitHubAuthRequest synapse, CancellationToken cancellationToken)
    {
        var store = Grains.GetGrain<IGitHubCredentialStore>(ProjectKey);

        if (!string.IsNullOrEmpty(synapse.PersonalAccessToken))
        {
            var bytes = Encoding.UTF8.GetBytes(synapse.PersonalAccessToken);
            var encrypted = tokenProtector.Protect(bytes);
            await store.SetEncryptedTokenAsync(encrypted);

            await FireResponseAsync(synapse, authenticated: true);
            return;
        }

        var existing = await store.GetEncryptedTokenAsync();
        if (existing != null && existing.Length > 0)
        {
            await FireResponseAsync(synapse, authenticated: true);
            return;
        }

        // Return a mock Device Flow challenge if no token is saved
        await FireResponseAsync(synapse, authenticated: false, 
            verificationUrl: "https://github.com/login/device", 
            userCode: "GH-DEV-77");
    }

    public async Task HandleAsync(GitCommitRequest synapse, CancellationToken cancellationToken)
    {
        var success = await CommitAsync(synapse.Message, synapse.Files, synapse.AutoStage);
        var responseHeaders = new SynapseMetadata(
            SynapseId: SynapseId.New(),
            CorrelationId: synapse.Headers.CorrelationId,
            CausationId: new CausationId(synapse.Headers.SynapseId.Value),
            CallerNeuronId: new NeuronId(InstanceId.ToString()),
            CallerNeuronType: NeuronType,
            ReceiverNeuronId: synapse.Headers.CallerNeuronId,
            ReceiverNeuronType: synapse.Headers.CallerNeuronType ?? "External",
            Timestamp: DateTimeOffset.UtcNow
        );

        var response = new ApplyCodeEditResponse(Success: success,
            ErrorMessage: success ? null : "Failed to execute git commit operation") { Headers = responseHeaders };

        await FireSynapseAsync(response, cancellationToken);
    }

    public async Task HandleAsync(SubmitPullRequest synapse, CancellationToken cancellationToken)
    {
        var success = await SubmitPullRequestAsync(synapse.Title, synapse.Body, synapse.SourceBranch, synapse.TargetBranch, synapse.Draft);
        var responseHeaders = new SynapseMetadata(
            SynapseId: SynapseId.New(),
            CorrelationId: synapse.Headers.CorrelationId,
            CausationId: new CausationId(synapse.Headers.SynapseId.Value),
            CallerNeuronId: new NeuronId(InstanceId.ToString()),
            CallerNeuronType: NeuronType,
            ReceiverNeuronId: synapse.Headers.CallerNeuronId,
            ReceiverNeuronType: synapse.Headers.CallerNeuronType ?? "External",
            Timestamp: DateTimeOffset.UtcNow
        );

        var response = new ApplyCodeEditResponse(Success: success,
            ErrorMessage: success ? null : "Failed to execute gh pr create operation") { Headers = responseHeaders };

        await FireSynapseAsync(response, cancellationToken);
    }

    public async Task HandleAsync(GitStatusRequest synapse, CancellationToken cancellationToken)
    {
        var status = await GetStatusAsync();
        var responseHeaders = new SynapseMetadata(
            SynapseId: SynapseId.New(),
            CorrelationId: synapse.Headers.CorrelationId,
            CausationId: new CausationId(synapse.Headers.SynapseId.Value),
            CallerNeuronId: new NeuronId(InstanceId.ToString()),
            CallerNeuronType: NeuronType,
            ReceiverNeuronId: synapse.Headers.CallerNeuronId,
            ReceiverNeuronType: synapse.Headers.CallerNeuronType ?? "External",
            Timestamp: DateTimeOffset.UtcNow
        );

        var response = status with
        {
            Headers = responseHeaders
        };

        await FireSynapseAsync(response, cancellationToken);
    }

    private async Task FireResponseAsync(GitHubAuthRequest request, bool authenticated, string? verificationUrl = null, string? userCode = null, string? errorMessage = null)
    {
        var responseHeaders = new SynapseMetadata(
            SynapseId: SynapseId.New(),
            CorrelationId: request.Headers.CorrelationId,
            CausationId: new CausationId(request.Headers.SynapseId.Value),
            CallerNeuronId: new NeuronId(InstanceId.ToString()),
            CallerNeuronType: NeuronType,
            ReceiverNeuronId: request.Headers.CallerNeuronId,
            ReceiverNeuronType: request.Headers.CallerNeuronType ?? "External",
            Timestamp: DateTimeOffset.UtcNow
        );

        var response = new GitHubAuthResponse(Authenticated: authenticated,
            VerificationUrl: verificationUrl,
            UserCode: userCode,
            ErrorMessage: errorMessage) { Headers = responseHeaders };

        await FireSynapseAsync(response);
    }
}
