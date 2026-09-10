using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Microsoft.GitHub;

[assembly: NeuronJsonContext(typeof(GitHubJson))]

namespace DigitalBrain.Microsoft.GitHub;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ConnectRepository))]
[JsonSerializable(typeof(RefreshRepository))]
[JsonSerializable(typeof(RegisterGitHubConnection))]
[JsonSerializable(typeof(ResolveRepository))]
[JsonSerializable(typeof(ReadPullRequest))]
[JsonSerializable(typeof(ReadReviewEvidence))]
[JsonSerializable(typeof(RepositoryView))]
[JsonSerializable(typeof(PullRequestRead))]
[JsonSerializable(typeof(PullRequestsRead))]
[JsonSerializable(typeof(ReviewEvidenceRead))]
[JsonSerializable(typeof(RequiredChecksRead))]
[JsonSerializable(typeof(GitHubConnectionRecord))]
[JsonSerializable(typeof(GitHubConnectionList))]
[JsonSerializable(typeof(GitHubSetupResult))]
[JsonSerializable(typeof(RepositoryEvent))]
[JsonSerializable(typeof(PullRequestChanged))]
[JsonSerializable(typeof(RepositoryAccessRevoked))]
[JsonSerializable(typeof(Accepted<RepositoryView>))]
[JsonSerializable(typeof(Accepted<GitHubConnectionRecord>))]
public sealed partial class GitHubJson : JsonSerializerContext;
