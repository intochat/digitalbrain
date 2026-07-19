using DigitalBrain;

internal sealed class QuickstartConsole(
    DigitalBrainClient client,
    TextReader input,
    TextWriter output,
    Func<ConversationId> newConversation)
{
    private readonly QuickstartCommands _commands = new(newConversation);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await output.WriteLineAsync(
            "DigitalBrain quickstart. Type /help for commands.");
        while (!cancellationToken.IsCancellationRequested)
        {
            await output.WriteAsync("> ");
            var line = await input.ReadLineAsync(cancellationToken);
            if (line is null)
                return;

            var command = _commands.Apply(line);
            if (command.Handled)
            {
                if (!string.IsNullOrEmpty(command.Message))
                    await output.WriteLineAsync(command.Message);
                if (command.Exit)
                    return;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var turnId = ConversationTurnId.New();
            var result = _commands.Role switch
            {
                ConversationRole.Fast => await client.Conversations
                    .Fast(_commands.Conversation)
                    .SubmitTurnAsync(turnId, line),
                ConversationRole.Balanced => await client.Conversations
                    .Balanced(_commands.Conversation)
                    .SubmitTurnAsync(turnId, line),
                ConversationRole.Reasoning => await client.Conversations
                    .Reasoning(_commands.Conversation)
                    .SubmitTurnAsync(turnId, line),
                _ => throw new InvalidOperationException(
                    "A declared conversation role is required.")
            };
            await output.WriteLineAsync(result.Response);
        }
    }
}

internal sealed class QuickstartCommands(Func<ConversationId> newConversation)
{
    public const string Help =
        "commands:/role /new /conversation /help /exit";

    public ConversationRole Role { get; private set; } =
        ConversationRole.Balanced;

    public ConversationId Conversation { get; private set; } =
        new("main");

    public QuickstartCommandResult Apply(string input)
    {
        var value = input.Trim();
        if (!value.StartsWith("/", StringComparison.Ordinal))
            return new QuickstartCommandResult(false, false, string.Empty);

        var parts = value.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts[0].ToLowerInvariant() switch
        {
            "/role" => ApplyRole(parts),
            "/new" => ApplyNew(parts),
            "/conversation" => ApplyConversation(parts),
            "/help" when parts.Length == 1 =>
                new QuickstartCommandResult(true, false, Help),
            "/exit" when parts.Length == 1 =>
                new QuickstartCommandResult(true, true, "exit"),
            _ => new QuickstartCommandResult(
                true,
                false,
                "unknown-command:/help")
        };
    }

    private QuickstartCommandResult ApplyRole(string[] parts)
    {
        if (parts.Length == 1)
            return Result($"role:{Role.ToString().ToLowerInvariant()}");
        if (parts.Length != 2 ||
            !Enum.TryParse<ConversationRole>(
                parts[1],
                ignoreCase: true,
                out var role) ||
            !Enum.IsDefined(role))
            return Result("usage:/role fast|balanced|reasoning");

        Role = role;
        return Result($"role:{Role.ToString().ToLowerInvariant()}");
    }

    private QuickstartCommandResult ApplyNew(string[] parts)
    {
        if (parts.Length != 1)
            return Result("usage:/new");
        Conversation = newConversation();
        return Result($"conversation:{Conversation}");
    }

    private QuickstartCommandResult ApplyConversation(string[] parts)
    {
        if (parts.Length == 1)
            return Result($"conversation:{Conversation}");
        if (parts.Length != 2)
            return Result("usage:/conversation [id]");

        try
        {
            Conversation = new ConversationId(parts[1]);
            return Result($"conversation:{Conversation}");
        }
        catch (ArgumentException)
        {
            return Result("usage:/conversation [id]");
        }
    }

    private static QuickstartCommandResult Result(string message) =>
        new(true, false, message);
}

internal readonly record struct QuickstartCommandResult(
    bool Handled,
    bool Exit,
    string Message);
