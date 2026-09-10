using System.Globalization;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using McpOperations = DigitalBrain.Mcp.BrainOperations;

namespace DigitalBrain.AI;

// A Session neuron with a model attached. Instruct is its configuration, held as
// latest-per-type; Ask is answered with Reply and Turn with Said,
// both on the incoming correlation. Everything else is journaled and ignored.
[GrainType(AIVocabulary.AgentType)]
internal sealed class AgentNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<AgentState>> state)
    : Neuron<AgentState>(runtime, state)
{
    // Descriptors are fixed for the life of the silo, so the typed functions an Instruct.tools
    // entry generates are built once per activation. Neuron turns are serialized: no locking.
    private readonly Dictionary<string, AIFunction[]> _typedFunctions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _takenFunctionNames = new(StringComparer.Ordinal);

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        if (delivery.Signal.Type is not (AIVocabulary.Ask or AIVocabulary.Turn))
        {
            return;
        }

        if (State?.Answered.Contains(delivery.SignalId) == true)
        {
            return;
        }

        // A tool runs on whatever thread the function-invocation loop happens to be on. Grain
        // state may only be touched on the grain's own scheduler, so every tool that reaches the
        // graph hops back to the scheduler this turn started on.
        var turnScheduler = TaskScheduler.Current;
        var instruct = Bodies.Instruct(await LatestBodyAsync(AIVocabulary.Instruct).ConfigureAwait(true));

        IChatClient client;
        try
        {
            client = Providers.Resolve(ServiceProvider, instruct.Provider, instruct.Model);
        }
        catch (SignalRejectedException rejected)
        {
            // Advice, not a crash: a thrown reaction would be retried forever with the same
            // error, and the asker would never hear why.
            await ReplyAsync(delivery, rejected.Message, cancellationToken).ConfigureAwait(true);
            return;
        }

        try
        {
            var invoker = ServiceProvider.GetRequiredService<INeuronInvoker>();
            var nativeTools = ServiceProvider.GetRequiredService<NativeTools>();
            var operations = new McpOperations(GrainFactory, invoker);
            var tools = new List<AITool>(BrainTools.For(this, operations, invoker)
                .Concat(instruct.Tools.Where(name => !nativeTools.Contains(name)).Distinct(StringComparer.Ordinal)
                    .SelectMany(name => TypedFunctionsFor(invoker, name)))
                .Select(function => new TurnBoundFunction(function, turnScheduler)));
            tools.AddRange(nativeTools.Resolve(instruct.Tools));

            var agent = new ChatClientAgent(
                client,
                new ChatClientAgentOptions
                {
                    Name = Id.Name,
                    ChatOptions = new ChatOptions
                    {
                        Instructions = instruct.System,
                        Tools = tools,
                        ModelId = instruct.Model,
                    },
                },
                ServiceProvider.GetService<ILoggerFactory>(),
                ServiceProvider);

            var correlation = delivery.CorrelationId.ToString();
            var session = await LoadSessionAsync(agent, correlation, cancellationToken).ConfigureAwait(true);
            var input = delivery.Signal.Type == AIVocabulary.Turn
                ? await TurnContextAsync(delivery).ConfigureAwait(true)
                : Bodies.Text(delivery.Signal.Body);

            var response = await agent.RunAsync(input, session, options: null, cancellationToken).ConfigureAwait(true);

            var text = response.Text ?? string.Empty;
            var isTurn = delivery.Signal.Type == AIVocabulary.Turn;
            await FireAsync(
                Signal.Create(isTurn ? AIVocabulary.Said : AIVocabulary.Reply, isTurn ? Bodies.Said(Id.ToString(), text) : Bodies.Write(text)),
                delivery.Source,
                delivery.CorrelationId,
                cancellationToken).ConfigureAwait(true);
            // Write the session with the marker after the answer, so a retry after activation loss re-asks rather than going silent.
            await SaveAnsweredTurnAsync(agent, correlation, session, delivery.SignalId, cancellationToken).ConfigureAwait(true);
        }
        // A model that times out cancels with a TaskCanceledException that has nothing to do
        // with this turn's token. Letting it escape would leave the cursor in place and the
        // drain would retry the same timeout forever, so it answers like any other failure.
        catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A failure to reach or persist escapes instead: the drain retries the whole
            // reaction, where answering here would end the conversation over a passing fault.
            if (TransientFailure.Covers(failure))
            {
                throw;
            }

            ServiceProvider.GetService<ILogger<AgentNeuron>>()?.LogError(failure, "Agent {Neuron} failed to answer.", Id);
            await ReplyAsync(delivery, failure.Message, cancellationToken).ConfigureAwait(true);
        }
    }

    private AIFunction[] TypedFunctionsFor(INeuronInvoker invoker, string name)
    {
        if (_typedFunctions.TryGetValue(name, out var functions))
        {
            return functions;
        }

        // A tools entry that is neither a native tool nor a neuron name contributes nothing,
        // for the same reason NativeTools skips a name nobody registered.
        functions = NeuronId.TryParse(name, out var neuron)
            ? [.. TypedNeuronFunctions.For(invoker, neuron, _takenFunctionNames)]
            : [];
        _typedFunctions.Add(name, functions);
        return functions;
    }

    // ---- what the tools call ----

    internal async Task<string> FireSignalAsync(string type, string body, string? to, string? correlation)
    {
        NeuronId? target = null;
        if (!string.IsNullOrWhiteSpace(to))
        {
            target = NeuronId.TryParse(to, out var parsed)
                ? parsed
                : throw new ArgumentException($"'{to}' is not a neuron name. Use a bare name such as 'run-tests' or 'type:name'; no spaces.", nameof(to));
        }

        CorrelationId? tie = null;
        if (!string.IsNullOrWhiteSpace(correlation))
        {
            tie = Guid.TryParse(correlation, out var value)
                ? new CorrelationId(value)
                : throw new ArgumentException($"'{correlation}' is not a correlation id.", nameof(correlation));
        }

        // In-process, never a grain call to self: the agent is already inside its own turn.
        var outcome = await FireAsync(Signal.Create(type, body), target, tie).ConfigureAwait(true);
        return JsonSerializer.Serialize(
            new Mcp.FireResult(outcome.SignalId.ToString(), outcome.CorrelationId.ToString(), outcome.Delivered, outcome.Busy),
            ToolJson);
    }

    internal async Task<string> ChangeSynapseAsync(string from, string to, string type, bool connect)
    {
        var source = Parse(from, nameof(from));
        var target = Parse(to, nameof(to));

        // A synapse carries a signal type, so the type must be vocabulary before the edge exists.
        _ = Signal.Create(type, "{}");

        if (source == Id)
        {
            await (connect ? Connect(target, type) : Disconnect(target, type)).ConfigureAwait(true);
        }
        else
        {
            var neuron = GrainFactory.GetGrain<INeuron>(source.ToGrainId());
            await (connect ? neuron.Connect(target, type) : neuron.Disconnect(target, type)).ConfigureAwait(true);
        }

        return $"{(connect ? "connected" : "disconnected")} {from} --{type}--> {to}";
    }

    // ---- the turn ----

    private static readonly JsonSerializerOptions ToolJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private async Task<string> LatestBodyAsync(string type)
    {
        var latest = await ReadState().ConfigureAwait(true);
        return latest.FirstOrDefault(d => d.Signal.Type == type)?.Signal.Body ?? string.Empty;
    }

    private Task ReplyAsync(SignalDelivery delivery, string text, CancellationToken cancellationToken)
        => FireAsync(Signal.Create(AIVocabulary.Reply, Bodies.Write(text)), delivery.Source, delivery.CorrelationId, cancellationToken);

    // A turn carries no transcript: the participant reads the source's incoming journal for
    // its own correlation and speaks next.
    private async Task<string> TurnContextAsync(SignalDelivery delivery)
    {
        var read = await GrainFactory.GetGrain<INeuron>(delivery.Source.ToGrainId())
            .ReadJournal(JournalKind.Incoming, 0)
            .ConfigureAwait(true);

        var context = new StringBuilder();
        foreach (var entry in read.Delta.Where(e => e.CorrelationId == delivery.CorrelationId))
        {
            if (entry.Signal.Type == AIVocabulary.Ask)
            {
                context.AppendLine(Bodies.Text(entry.Signal.Body));
            }
        }

        foreach (var entry in read.Delta.Where(e => e.CorrelationId == delivery.CorrelationId && e.Signal.Type == AIVocabulary.Said))
        {
            var said = Bodies.SaidLine(entry.Signal.Body);
            if (said.Length > 0)
            {
                context.AppendLine(said);
            }
        }

        context.Append(CultureInfo.InvariantCulture, $"You are {Id}. Speak next.");
        return context.ToString();
    }

    // ---- sessions ----

    private async Task<AgentSession> LoadSessionAsync(AIAgent agent, string correlation, CancellationToken cancellationToken)
    {
        if (State?.Sessions.FirstOrDefault(s => string.Equals(s.Correlation, correlation, StringComparison.Ordinal)) is { } entry)
        {
            using var document = JsonDocument.Parse(entry.SessionJson);
            return await agent.DeserializeSessionAsync(document.RootElement, cancellationToken: cancellationToken).ConfigureAwait(true);
        }

        return await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task SaveAnsweredTurnAsync(AIAgent agent, string correlation, AgentSession session, SignalId answered, CancellationToken cancellationToken)
    {
        var json = (await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken).ConfigureAwait(true)).GetRawText();
        var current = State;
        var sessions = current is null ? [] : new List<AgentSessionEntry>(current.Sessions);
        sessions.RemoveAll(s => string.Equals(s.Correlation, correlation, StringComparison.Ordinal));
        sessions.Add(new AgentSessionEntry(correlation, json));
        Trim(sessions, AgentState.MaxSessions);

        var answeredTurns = current is null ? [] : new List<SignalId>(current.Answered);
        answeredTurns.Add(answered);
        Trim(answeredTurns, AgentState.MaxAnswered);

        await SaveAsync(new AgentState(sessions, answeredTurns), cancellationToken).ConfigureAwait(true);
    }

    private static void Trim<T>(List<T> entries, int keep)
    {
        if (entries.Count > keep)
        {
            entries.RemoveRange(0, entries.Count - keep);
        }
    }

    // ---- plumbing ----

    private static NeuronId Parse(string text, string parameter)
        => NeuronId.TryParse(text, out var id)
            ? id
            : throw new ArgumentException($"'{text}' is not a neuron name. Use a bare name such as 'run-tests' or 'type:name'; no spaces.", parameter);
}
