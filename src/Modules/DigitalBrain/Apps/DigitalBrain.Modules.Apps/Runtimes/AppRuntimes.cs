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
    public string AuthoringDescription => """
        "group-chat": models discuss in rounds and the first participant writes the final answer.
        File groupchat.json: {"brief":"prompts/brief.md","participants":[{"name":"Luna","instructions":"prompts/luna.md"}]}.
        Include every named prompt file. Declare a "{Name}Model" setting for each participant,
        default IGpt56Luna for the first and IGemma4 for others; "MaxRounds" defaults to "3".
        In each round everyone speaks once in order. From round 2, all replies starting with AGREE end discussion.
        """ + $"\nFor discussion assertions use IGroupChat at {ChatKey("{scope}/app", Guid.Empty).Replace(Guid.Empty.ToString("N"), "{invocationId:N}", StringComparison.Ordinal)} and Read() for turns, rounds and agreement.";

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
    public string AuthoringDescription => "\"prompt\": one model answers with file prompts/system.md. Declare setting Model (default IGemma4).";

    public async Task<string> Answer(AppRuntimeRequest request, CancellationToken cancellationToken)
    {
        var model = request.Settings.GetValueOrDefault("Model") ?? throw new InvalidOperationException("Set Model to choose who answers.");
        var answer = await ModelAddress.Complete(grains, model, request.Content.File(SystemPrompt) ?? "", request.Input, cancellationToken);
        return answer.Length > 0 ? answer : throw new InvalidOperationException("The model returned an empty answer.");
    }
}

