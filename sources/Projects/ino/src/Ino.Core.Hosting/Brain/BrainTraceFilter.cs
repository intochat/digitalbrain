using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ino.Core.Brain;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Ino.Core.Hosting.Brain;

/// <summary>
/// Wraps every grain call and emits a <see cref="BrainPulse"/> on the
/// <c>ino-brain</c> stream. Silent on sink failure — the brain stream is
/// observability, not business logic. Reads identity from <see cref="RequestContext"/>;
/// falls back to <c>("system", "autonomic")</c> for system-internal hops.
/// </summary>
public sealed class BrainTraceFilter(
    IBrainPulseSink sink,
    ILogger<BrainTraceFilter> logger) : IIncomingGrainCallFilter
{
    // Test-only override (see BrainTraceFilterTests). Null in production.
    internal static string? MethodNameOverrideForTests { get; set; }

    // 1-second cap so an unresponsive stream provider can never stall a grain call.
    private static readonly TimeSpan EmitTimeout = TimeSpan.FromSeconds(1);

    private const int PayloadCapBytes = 4096;
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        WriteIndented = false,
    };

    private static string SerializePayload(IIncomingGrainCallContext context)
    {
        // Orleans 10 exposes arguments via IInvokable on context.Request.
        var request = context.Request;
        if (request is null || request.GetArgumentCount() == 0) return string.Empty;
        try
        {
            var firstArg = request.GetArgument(0);
            if (firstArg is null) return string.Empty;
            var json = JsonSerializer.Serialize(firstArg, PayloadJsonOptions);
            if (json.Length > PayloadCapBytes)
                return json[..PayloadCapBytes] + "…<truncated>";
            return json;
        }
        catch
        {
            // Best-effort observability — never fail the grain call because of this.
            return string.Empty;
        }
    }

    public async Task Invoke(IIncomingGrainCallContext context)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        Exception? caught = null;
        try
        {
            await context.Invoke();
        }
        catch (Exception ex)
        {
            caught = ex;
            throw;
        }
        finally
        {
            await EmitPulseAsync(context, startTimestamp, caught);
        }
    }

    private async Task EmitPulseAsync(
        IIncomingGrainCallContext context,
        long startTimestamp,
        Exception? caught)
    {
        try
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            var userId = (RequestContext.Get(InoRequestContextKeys.UserId) as string) ?? "system";
            var sessionId = (RequestContext.Get(InoRequestContextKeys.SessionId) as string)
                ?? InoRequestContextKeys.AutonomicSessionId;

            var pulse = new BrainPulse(
                TraceParent: Activity.Current?.Id ?? string.Empty,
                InoInstanceId: sessionId,
                UserId: userId,
                FromGrain: string.Empty, // best-effort; RuntimeContext is internal to Orleans
                ToGrain: context.TargetContext?.GrainId.ToString() ?? string.Empty,
                MethodName: MethodNameOverrideForTests ?? context.ImplementationMethod?.Name ?? string.Empty,
                DurationMs: (long)elapsed.TotalMilliseconds,
                Status: caught is null ? BrainPulseStatus.Ok : BrainPulseStatus.Failed,
                TimestampUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                PayloadJson: SerializePayload(context));

            // Cap the emit so an unresponsive stream provider never stalls the grain call.
            using var cts = new CancellationTokenSource(EmitTimeout);
            await sink.EmitAsync(pulse, cts.Token).WaitAsync(cts.Token);
        }
        catch (Exception emitError)
        {
            // Brain stream is best-effort. A sink failure must never bubble up
            // and abort a grain call.
            logger.LogDebug(emitError, "BrainTraceFilter sink emit failed; pulse dropped.");
        }
    }
}
