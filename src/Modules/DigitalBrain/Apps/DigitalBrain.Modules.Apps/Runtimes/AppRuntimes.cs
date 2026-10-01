using System.Globalization;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.GroupChat;
using DigitalBrain.Apps;

namespace DigitalBrain.Apps;

// A group chat app is IGroupChat configured from groupchat.json, its prompt files and its settings:
// each participant speaks with the model named by the "{Name}Model" setting.
public sealed class GroupChatRuntime(IGrainFactory grains) : IAppRuntime
{
    public const string RuntimeName = "group-chat";
    private const string ConfigFile = "groupchat.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => RuntimeName;

    public static string ChatKey(string appKey, Guid invocationId) => $"{appKey}/chat/{invocationId:N}";

    public async Task<string> Answer(AppRuntimeRequest request, CancellationToken cancellationToken)
    {
        var config = JsonSerializer.Deserialize<GroupChatJson>(request.Content.File(ConfigFile)
            ?? throw new InvalidOperationException($"A group chat app needs {ConfigFile}."), Json)
            ?? throw new InvalidOperationException($"{ConfigFile} is empty.");
        var participants = config.Participants.Select(participant => new GroupChatParticipant(
            participant.Name,
            request.Settings.GetValueOrDefault(participant.Name + "Model") ?? throw new InvalidOperationException($"Set {participant.Name}Model to choose who speaks as {participant.Name}."),
            PromptFile(request.Content, participant.Instructions))).ToArray();
        var maxRounds = int.TryParse(request.Settings.GetValueOrDefault("MaxRounds"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rounds) ? rounds : 3;
        var chat = grains.GetGrain<IGroupChat>(ChatKey(request.AppKey, request.InvocationId));
        await chat.Configure(new GroupChatSetup(participants, PromptFile(request.Content, config.Brief), maxRounds));
        return (await chat.Ask(request.Input)).Answer;
    }

    private static string PromptFile(PackageContent content, string? path)
        => path is null ? "" : content.File(path) ?? throw new InvalidOperationException($"The app has no {path}.");

    private sealed record GroupChatJson(string? Brief, IReadOnlyList<ParticipantJson> Participants);
    private sealed record ParticipantJson(string Name, string? Instructions);
}

// A prompt app answers with one model, the one named by its "Model" setting, and prompts/system.md.
public sealed class PromptRuntime(IGrainFactory grains) : IAppRuntime
{
    private const string SystemPrompt = "prompts/system.md";

    public string Name => "prompt";

    public async Task<string> Answer(AppRuntimeRequest request, CancellationToken cancellationToken)
    {
        var model = request.Settings.GetValueOrDefault("Model") ?? throw new InvalidOperationException("Set Model to choose who answers.");
        var answer = await ModelAddress.Complete(grains, model, request.Content.File(SystemPrompt) ?? "", request.Input, cancellationToken);
        return answer.Length > 0 ? answer : throw new InvalidOperationException("The model returned an empty answer.");
    }
}

