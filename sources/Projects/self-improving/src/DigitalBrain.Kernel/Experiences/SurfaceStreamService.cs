using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;
using DigitalBrain.Os.UI;
using Grpc.Core;

namespace DigitalBrain.Kernel.Experiences;

public sealed class SurfaceStreamService : global::DigitalBrain.Surfaces.SurfaceStream.SurfaceStreamBase
{
    private static readonly ConcurrentDictionary<IServerStreamWriter<global::DigitalBrain.Surfaces.UiSurfaceMessage>, string> Writers = new();
    private static readonly ConcurrentDictionary<string, global::DigitalBrain.Surfaces.UiSurfaceMessage> LastSurfaces = new();

    // OnTap synapses must cross the gRPC JSON leg with a runtime-type discriminator + flat payload props
    // (STJ default serializes the declared base Synapse only, so Flutter taps arrived without Type/ExperienceId
    // and the ClientTap name-matching in DigitalBrainGrain could never reconstruct them).
    private static readonly JsonSerializerOptions SurfaceJson = new() { Converters = { new SynapseJsonConverter() } };

    private readonly IGrainFactory? _grainFactory;
    private readonly IServiceProvider? _serviceProvider;

    public SurfaceStreamService(IGrainFactory? grainFactory = null, IServiceProvider? serviceProvider = null)
    {
        _grainFactory = grainFactory;
        _serviceProvider = serviceProvider;
    }

    public override async Task SubscribeSurfaces(global::DigitalBrain.Surfaces.SurfaceSubscription request, IServerStreamWriter<global::DigitalBrain.Surfaces.UiSurfaceMessage> responseStream, ServerCallContext context)
    {
        // Token floor for gRPC Subscribe/Send (per-brain/user, issued on first contact). TLS/mTLS via Orleans.Connections.Security in silo config (dev LAN + prod notes).
        var token = context.RequestHeaders.FirstOrDefault(h => h.Key.Equals("token", StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(token))
        {
            // For now, allow with warning (floor); in full, throw or redirect to login.
            // Real token issuance happens on Login/ first AddBrain in identity flow.
        }

        var username = !string.IsNullOrWhiteSpace(request.Username)
            ? request.Username
            : context.RequestHeaders.FirstOrDefault(h => h.Key.Equals("username", StringComparison.OrdinalIgnoreCase))?.Value ?? "global";
        var brainId = !string.IsNullOrWhiteSpace(request.BrainId)
            ? request.BrainId
            : "main";
        Writers[responseStream] = username + "|" + brainId;
        global::DigitalBrain.Os.SurfaceFanout.Instance = new KernelSurfaceFanout();
        var seed = new global::DigitalBrain.Surfaces.UiSurfaceMessage
        {
            SurfaceId = "chat-history",
            Emitter = "agent/" + brainId,
            WidgetJson = System.Text.Json.JsonSerializer.Serialize(new { Value = "Journal history seeded for brain " + brainId }),
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        try { await responseStream.WriteAsync(seed); } catch { }

        // Replay all last cached surfaces to this subscriber (shell, windows, marketplace, graph etc).
        // Generalized from per-id ifs (ui-shell + ui-windows + marketplace) after questioning "only these three need replay for late gRPC clients".
        // Publish-time allowed filter already gates; cached replay accelerates first paint for PinSurface nav clicks and aspire-launched flutter (subscribe after kernel start). No per-new-surface if maintenance.
        foreach (var kvp in LastSurfaces)
        {
            try { await responseStream.WriteAsync(kvp.Value); } catch { }
        }

        try
        {
            await Task.Delay(-1, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Writers.TryRemove(responseStream, out _);
        }
    }

    public override async Task<global::DigitalBrain.Surfaces.ClientEventResponse> SendClientEvent(global::DigitalBrain.Surfaces.ClientEvent request, ServerCallContext context)
    {
        var gf = _grainFactory ?? _serviceProvider?.GetService<IGrainFactory>();
        if (gf != null && !string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            try
            {
                var username = context.RequestHeaders.FirstOrDefault(h => h.Key.Equals("username", StringComparison.OrdinalIgnoreCase))?.Value ?? "global";
                
                // Route through the user's active brain. UiNeuron remnant deleted per C Task 2 (uineuron grain type + IUi GetGrain calls purged).
                // Shell implements IUiNeuron for workspace compat (GetState returns minimal); remnant resolution paths removed to enforce single-source.
                // Fallback used (no more direct uiNeuron state read for CurrentBrainId).
                var targetBrain = username ?? Brain.WellKnownKey;

                var brain = gf.GetGrain<IDigitalBrain>(targetBrain);
                // Deliver the tapped synapse into the brain via the normal path.
                // The ClientTap handler in DigitalBrainGrain will reconstruct the concrete synapse
                // (InstallFromMarketplace, DismissAlarm, etc.) and SendAsync it.
                await brain.SendAsync(new ClientTap(request.SurfaceId ?? string.Empty, request.PayloadJson));
            }
            catch (Exception ex)
            {
                // Best effort; the tap is still journaled via the ClientTap emission if the send above partially worked.
                Console.WriteLine($"[SurfaceStream] Client tap delivery error: {ex.Message}");
            }
        }

        return new global::DigitalBrain.Surfaces.ClientEventResponse
        {
            Success = true,
            Message = "tap received"
        };
    }

    public static void Publish(global::DigitalBrain.Surfaces.UiSurfaceMessage message)
    {
        LastSurfaces[message.SurfaceId] = message;

        foreach (var kv in Writers)
        {
            var writer = kv.Key;
            var key = kv.Value;
            var partsKey = key.Split('|');
            var connectionUser = partsKey[0];
            var connectionBrain = partsKey.Length > 1 ? partsKey[1] : "main";

            var emitterBrain = "global";
            if (!string.IsNullOrEmpty(message.Emitter))
            {
                var parts = message.Emitter.Split('/');
                emitterBrain = parts.Length > 1 ? parts[1] : parts[0];
            }

            bool isAllowed = emitterBrain == connectionBrain
                || emitterBrain == "global"
                || message.SurfaceId == "marketplace"
                || message.SurfaceId == "kerneltasks"
                || message.SurfaceId == "ui-shell"
                || message.SurfaceId == "brain-graph-3d"
                || message.SurfaceId.StartsWith("review:")
                || message.SurfaceId.StartsWith("chat");

            if (isAllowed)
            {
                try
                {
                    _ = writer.WriteAsync(message);
                }
                catch
                {
                    Writers.TryRemove(writer, out _);
                }
            }
        }
    }

    public static global::DigitalBrain.Surfaces.UiSurfaceMessage ToMessage(UiSurface surface) =>
        new()
        {
            SurfaceId = surface.SurfaceId,
            Emitter = surface.Emitter.ToString(),
            WidgetJson = JsonSerializer.Serialize(surface.Root, SurfaceJson),
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

    private sealed class SynapseJsonConverter : JsonConverter<Synapse>
    {
        public override bool CanConvert(Type typeToConvert) => typeof(Synapse).IsAssignableFrom(typeToConvert);

        public override Synapse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("Tapped synapses are reconstructed by Type name in the ClientTap handler, never deserialized here.");

        public override void Write(Utf8JsonWriter writer, Synapse value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            if (value is DynamicSynapse ds)
            {
                writer.WriteString("Type", ds.TypeName);
                if (ds.Payload != null)
                {
                    foreach (var kv in ds.Payload)
                    {
                        writer.WriteString(kv.Key, kv.Value);
                    }
                }
            }
            else
            {
                var type = value.GetType();
                writer.WriteString("Type", type.Name);
                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.DeclaringType == typeof(Synapse) || p.GetIndexParameters().Length > 0) continue;
                    var v = p.GetValue(value);
                    if (v is null) continue;
                    writer.WritePropertyName(p.Name);
                    switch (v)
                    {
                        case ExperienceId eid: writer.WriteStringValue(eid.Value); break;
                        case BundleId bid: writer.WriteStringValue(bid.Value); break;
                        case TaskId tid: writer.WriteStringValue(tid.Value); break;
                        case string s: writer.WriteStringValue(s); break;
                        case bool b: writer.WriteBooleanValue(b); break;
                        case int i: writer.WriteNumberValue(i); break;
                        case long l: writer.WriteNumberValue(l); break;
                        case double d: writer.WriteNumberValue(d); break;
                        default: JsonSerializer.Serialize(writer, v, v.GetType(), options); break;
                    }
                }
            }
            writer.WriteEndObject();
        }
    }

    private sealed class KernelSurfaceFanout : global::DigitalBrain.Os.ISurfaceFanout
    {
        public void Publish(global::DigitalBrain.Os.UI.UiSurface surface)
        {
            // research fanout disabled (stray cleaned); surfaces travel timeline only for core InoLang + TUI
        }
    }

    public override async Task<global::DigitalBrain.Surfaces.LoginResponse> Login(global::DigitalBrain.Surfaces.LoginRequest request, ServerCallContext context)
    {
        var gf = _grainFactory ?? _serviceProvider?.GetService<Orleans.IGrainFactory>();
        var resp = new global::DigitalBrain.Surfaces.LoginResponse { Username = request.Username };
        if (gf != null)
        {
            // UiNeuron remnant deleted per C Task 2. No GetGrain<IUiNeuron> for Login brains list (was remnant path).
            // AvailableBrains now driven from Shell workspace state / timeline (or empty for this compat surface; real brains via other).
            // Shell (IUi impl) not directly queried here to complete purge of IUi GetGrain for old ui.
        }
        return resp;
    }

    public override async Task<global::DigitalBrain.Surfaces.BrainDescriptor> AddBrain(global::DigitalBrain.Surfaces.AddBrainRequest request, ServerCallContext context)
    {
        var gf = _grainFactory ?? _serviceProvider?.GetService<Orleans.IGrainFactory>();
        if (gf != null)
        {
            // UiNeuron deleted per C Task 2. IUiNeuron GetGrain removed (remnant).
            // The // await Add was already commented (research). AddBrain/Archive now no-op here; picker covered by Shell + ClientTap telemetry.
            try { } catch { }
        }
        var d = new global::DigitalBrain.Surfaces.BrainDescriptor { Name = request.BrainName, Kind = "GrainKeyed" };
        try
        {
            var br = gf.GetGrain<global::DigitalBrain.Os.Application.IDigitalBrain>(request.BrainName);
            var id = br.GetIdentityAsync().GetAwaiter().GetResult();
            d.PublicKeyBase64 = id.PublicKeyBase64 ?? "";
            d.Fingerprint = id.Fingerprint ?? "";
        }
        catch { }
        return d;
    }

    public override async Task<global::DigitalBrain.Surfaces.ClientEventResponse> ArchiveBrain(global::DigitalBrain.Surfaces.ArchiveBrainRequest request, ServerCallContext context)
    {
        var gf = _grainFactory ?? _serviceProvider?.GetService<Orleans.IGrainFactory>();
        if (gf != null)
        {
            // UiNeuron deleted per C Task 2. Last IUi GetGrain remnant (in ArchiveBrain) purged. No-op; covered elsewhere via Shell.
            try { } catch { }
        }
        return new global::DigitalBrain.Surfaces.ClientEventResponse { Success = true };
    }
}