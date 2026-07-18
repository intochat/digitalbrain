using Ino.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Ino.Core.Hosting.Llm;

/// <summary>
/// Test-mode <see cref="IChatClientFactory"/> that returns the same
/// <see cref="BddMockChatClient"/> for every tier. Tier-fallback semantics
/// don't matter for a mock — every prompt routes through the regex-indexed
/// scenario list regardless of which tier the caller asked for. Registered
/// in <see cref="AddInoChatClientsExtensions.AddInoChatClients"/> when the
/// <c>INO_TEST_MODE</c> env var is <c>true</c>.
/// </summary>
public sealed class BddMockChatClientFactory : IChatClientFactory
{
    readonly BddMockChatClient _client;
    readonly LlmModel[] _models;

    public BddMockChatClientFactory(
        IEnumerable<BddScenario> scenarios,
        IReasoningProbe probe,
        ILogger<BddMockChatClient>? log = null,
        TimeProvider? time = null)
    {
        _client = new BddMockChatClient(scenarios, probe, log, time);
        _models = [BddMockModel.Instance];
    }

    public IReadOnlyList<LlmModel> RegisteredModels => _models;

    public IChatClient ForTier(LlmTier tier)
    {
        if (tier == LlmTier.None)
            throw new ArgumentException(
                "LlmTier.None is not a valid request — callers must ask for Fast/Balanced/Reasoning.",
                nameof(tier));
        return _client;
    }

    sealed class BddMockModel : LlmModel
    {
        public static readonly BddMockModel Instance = new();
        public override string Provider => "bdd-mock";
        public override string Id => "bdd-mock";
        public override string DisplayName => "BDD Mock";
        public override LlmTier DefaultTier => LlmTier.Balanced;
    }
}
