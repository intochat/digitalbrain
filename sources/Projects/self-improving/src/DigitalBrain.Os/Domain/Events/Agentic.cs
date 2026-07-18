using DigitalBrain.Protocol.Domain.Events;
namespace DigitalBrain.Os.Domain.Events;

[GenerateSerializer]
public sealed record ToolInvokePayload(string ToolName, string ArgumentsJson, string CallId);

[GenerateSerializer]
public sealed record ToolCompletePayload(string CallId, string ResultJson, bool Success);

// union ToolCall(ToolInvokePayload, ToolCompletePayload); // commented (and its GenerateSerializer) to allow subsequent records (ForkBrain etc) in ns to compile; union syntax not standard C# in this preview and was blocking type resolution for distribution/rule high-sev paths.

[GenerateSerializer]
public sealed record ToolInvokeSynapse(ToolInvokePayload Invoke) : Synapse;

[GenerateSerializer]
public sealed record ToolResultSynapse(ToolCompletePayload Result) : Synapse;

[GenerateSerializer]
public sealed record PlanThink(string Thought);

[GenerateSerializer]
public sealed record PlanAct(string Action, string? Detail = null);

[GenerateSerializer]
public sealed record PlanObserve(string Observation);

[GenerateSerializer]
public union PlanStep(PlanThink, PlanAct, PlanObserve);

[GenerateSerializer]
public sealed record AgentPlanStep(string PlanId, PlanStep Step) : Synapse;

[GenerateSerializer]
public sealed record RememberSynapse(string Key, string Value, string? CorrelationScope = null) : Synapse;

[GenerateSerializer]
public sealed record RecallQuerySynapse(string Key, string? CorrelationScope = null) : Synapse;

[GenerateSerializer]
public sealed record RecallHit(string Key, string Value);

[GenerateSerializer]
public sealed record RecallMiss(string Key);

[GenerateSerializer]
public union RecallResult(RecallHit, RecallMiss);

[GenerateSerializer]
public sealed record MemoryRecallSynapse(RecallQuerySynapse Query, RecallResult Result) : Synapse;

[GenerateSerializer]
public sealed record ActionInstallExperience(string ExperienceId);

[GenerateSerializer]
public sealed record ActionRunSimulation(string SimulationName, string? TargetDomainId = null);

[GenerateSerializer]
public sealed record ActionEmitSynapse(string SynapseType, string PayloadJson);

[GenerateSerializer]
public sealed record ActionCreateIno(string InoPath, string Content);

[GenerateSerializer]
public union ImprovementAction(ActionInstallExperience, ActionRunSimulation, ActionEmitSynapse, ActionCreateIno);

[GenerateSerializer]
public sealed record ApproveAction(ImprovementAction? Action) : Synapse;

// Flutter client lifecycle + back-channel (controlled by IFlutter neuron, modeled on IAspire).
// StartFlutterClient (in Sdk.Microsoft.Flutter) is the primary "brain tells the system to build+run the Flutter renderer"
// (Aspire resource when under DistributedApplication, best-effort spawn otherwise).
// The older StartFlutter / Flutter* events are retained for client-to-brain intents (taps, gestures) and status.
[GenerateSerializer]
public sealed record StartFlutter(string Target = "") : Synapse;

[GenerateSerializer]
public sealed record FlutterStarted(string Target, bool Success, string? Message = null) : Synapse;

[GenerateSerializer]
public sealed record FlutterUiEvent(string Type, string? Payload = null) : Synapse;

// Alarm scenario synapses: "set alarm in 10 mins" from user/LLM/agent -> produces AlarmSet + UiSurface widget.
// Handler (e.g. in FlutterNeuron for UI surfaces or dedicated alarm experience) reacts, emits the surface so TaskManager + alarm widget appear by default in clients.
// Dismiss carries intent synapse back via OnTap on the Button in the surface (roundtrips as client event or direct Send).
[GenerateSerializer]
public sealed record SetAlarm(int Minutes, string Label) : Synapse;

[GenerateSerializer]
public sealed record AlarmSet(string AlarmId, DateTimeOffset FiresAt, string Label) : Synapse;

[GenerateSerializer]
public sealed record DismissAlarm(string AlarmId) : Synapse;

[GenerateSerializer]
public sealed record AlarmFired(string AlarmId) : Synapse;

// Minimal synapses for the dedicated weather watcher experience (the "new handler" installed via bundle).
// WeatherWatcherNeuron handles WeatherQuery (can be broadcast or p2p after install) and emits WeatherResult with live https data.
[GenerateSerializer]
public sealed record WeatherQuery(string Location, string? Units = "metric") : Synapse;

[GenerateSerializer]
public sealed record WeatherResult(string Location, string Summary, string SourceUrl, DateTimeOffset RetrievedAt) : Synapse;

// Ino session synapses (valuable core from vision + 09 terminals plan): enable dedicated ino/chat/authoring sessions with per-session journal + UiSurfaces.
// "Ino session host" + terminals per sessionId. Supports cross-cluster distribution of sessions (install on one, session on other).
// Used by supervisor or dedicated neuron to emit surfaces; terminals subscribe by id.
[GenerateSerializer]
public sealed record InoSessionStarted(string SessionId, string Description) : Synapse;

[GenerateSerializer]
public sealed record InoSessionCommand(string SessionId, string Command, string? Payload = null) : Synapse;

[GenerateSerializer]
public sealed record InoSessionEnded(string SessionId, string? Reason = null) : Synapse;

// Back-channel from rich clients (Flutter) on Button tap. Payload = original OnTap SynapseJson embedded in UiWidget.
// DigitalBrainGrain reconstructs concrete synapse and SendAsync so it executes exactly as from TUI or other.
[GenerateSerializer]
public sealed record ClientTap(string SurfaceId, string SynapseJson) : Synapse;

// Voice recorder support: flutter mic -> bytes (via gRPC ClientEvent backchannel as 'voice' with base64) -> backend transcription.
// Transcribed text fed to AgentRequest for LLM chat (ino) or surfaces. Fits neuron/synapse, local like gemma.
[GenerateSerializer]
public sealed record VoiceMessageRecorded(byte[] AudioData, string MimeType, double? DurationSeconds) : Synapse;

[GenerateSerializer]
public sealed record TranscribedText(string Text, string? Language, float? Confidence) : Synapse;

[GenerateSerializer]
public sealed record ForkBrain(
    [property: Id(0)] string ParentBrainKey,
    [property: Id(1)] string NewBrainName,
    [property: Id(2)] DateTimeOffset? UpTo = null
) : Synapse;