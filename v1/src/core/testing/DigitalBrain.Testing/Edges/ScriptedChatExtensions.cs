using Microsoft.Extensions.AI;

namespace DigitalBrain.Testing;

public static class ScriptedChatExtensions
{
    public static ScriptedChatClient ConfigureScriptedChat(
        this DigitalBrainTestBuilder brain,
        params Type[] models)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(models);

        if (models.Length == 0)
        {
            throw new ArgumentException(
                "Scripted chat must name at least one model neuron type to script.",
                nameof(models));
        }

        var script = new ScriptedChatClient();
        brain.ConfigureChatClient<IChatClient, ScriptedChatClient>(
            models, script, script, static scripted => scripted.Reset());

        return script;
    }

    public static ScriptedChatClient Chat(this TestBrain brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        return brain.ChatClientScript<ScriptedChatClient>();
    }
}
