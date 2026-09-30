using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.FileInput;
using DigitalBrain.Flutter.Layout;
using DigitalBrain.Flutter.Select;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.Text;
using DigitalBrain.Flutter.TextField;
using DigitalBrain.Flutter.VoiceInput;

namespace DigitalBrain.Assistant;

// The assistant's edge neurons, composed by plain grain calls: the same wiring any behavior
// script could do through the contracts, kept idempotent so a re-run only rewrites the same state.
public static class AssistantSurface
{
    public static string Key(string workspace) => workspace + "/applications/assistant";

    public const string ThreadsPart = "threads";
    public const string ModelPart = "model";
    public const string MessagesPart = "messages";
    public const string DraftPart = "draft";
    public const string VoicePart = "voice";
    public const string AttachPart = "attachments";
    public const string SendPart = "send";
    public const string StopPart = "stop";
    public const string NewPart = "new-conversation";
    public const string StatusPart = "status";
    public const string ResultsPart = "results";
    public const string ReceiptsPart = "receipts";

    public static async Task Compose(IGrainFactory grains, string key)
    {
        string Name(string part) => UiParts.NameOf(key, part);
        UiChildRef Ref(string kind, string part) => new(kind, Name(part));
        Task Bind(string part) => grains.GetGrain<IUiBinding>(Name(part)).Bind(grains.GetGrain<IAssistant>(key));
        async Task Layout(string part, string mode, UiChildRef[] children, double[]? extents = null, bool followEnd = false)
        {
            var layout = grains.GetGrain<ILayout>(Name(part));
            await layout.Set(new(mode, children, Extents: extents, FollowEnd: followEnd), (await layout.Read()).Revision);
        }

        await grains.GetGrain<ISelect>(Name(ThreadsPart)).Set("Conversation", [], null);
        await Bind(ThreadsPart);
        await grains.GetGrain<IButton>(Name(NewPart)).Set("New", "new-conversation");
        await Bind(NewPart);
        await grains.GetGrain<ISelect>(Name(ModelPart)).Set("Model", [], null);
        await Bind(ModelPart);
        await Layout("toolbar", "row", [Ref(UIVocabulary.SelectType, ThreadsPart), Ref(UIVocabulary.ButtonType, NewPart), Ref(UIVocabulary.SelectType, ModelPart)], [0, 100, 0]);

        await Layout(MessagesPart, "list", []);
        await Layout(ResultsPart, "list", []);
        await Layout(ReceiptsPart, "list", []);
        await Layout("content", "list", [Ref(UIVocabulary.LayoutType, MessagesPart), Ref(UIVocabulary.LayoutType, ResultsPart), Ref(UIVocabulary.LayoutType, ReceiptsPart)], followEnd: true);

        await grains.GetGrain<IButton>(Name("help")).Set("What can you help me with?", "prompt:help");
        await Bind("help");
        await grains.GetGrain<IButton>(Name("data")).Set("Show me my data", "prompt:data");
        await Bind("data");
        await Layout("prompts", "row", [Ref(UIVocabulary.ButtonType, "help"), Ref(UIVocabulary.ButtonType, "data")]);

        await grains.GetGrain<IText>(Name(StatusPart)).Set("");
        await grains.GetGrain<ITextField>(Name(DraftPart)).Configure("Message", "multiline", Name(SendPart));
        await Bind(DraftPart);

        await grains.GetGrain<IFileInput>(Name(AttachPart)).Configure("Attach file");
        await Bind(AttachPart);
        await grains.GetGrain<IVoiceInput>(Name(VoicePart)).Configure("Hold to talk");
        await Bind(VoicePart);
        await grains.GetGrain<IButton>(Name(SendPart)).Set("Send", "submit");
        await Bind(SendPart);
        await grains.GetGrain<IButton>(Name(StopPart)).Set("Stop", "cancel");
        await Bind(StopPart);
        await Layout("actions", "row",
            [Ref(UIVocabulary.FileInputType, AttachPart), Ref(UIVocabulary.VoiceInputType, VoicePart), Ref(UIVocabulary.ButtonType, SendPart), Ref(UIVocabulary.ButtonType, StopPart)]);

        await Layout("main", "column",
            [Ref(UIVocabulary.LayoutType, "toolbar"), Ref(UIVocabulary.LayoutType, "content"), Ref(UIVocabulary.LayoutType, "prompts"), Ref(UIVocabulary.TextType, StatusPart), Ref(UIVocabulary.TextFieldType, DraftPart), Ref(UIVocabulary.LayoutType, "actions")],
            [64, 0, 40, 32, 100, 48]);

        var surface = grains.GetGrain<ISurface>(Name("surface"));
        await surface.Set(new("Assistant", [Ref(UIVocabulary.LayoutType, "main")]), (await surface.Read()).Revision);
    }
}
