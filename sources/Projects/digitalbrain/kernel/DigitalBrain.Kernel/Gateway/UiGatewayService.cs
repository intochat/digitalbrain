using DigitalBrain.Runtime.Grpc;
using Grpc.Core;

namespace DigitalBrain.Kernel.Gateway;

// Bidi UI session bridge (uigateway.proto). The Flutter canvas opens one
// EngageUiSession stream and pushes UiInputSynapse events (e.g. the auto-layout
// "tidy" action); the kernel answers with UiStateSignal viewport updates so the
// background graph camera/spring settles in step with the window manager (W-5).
//
// This lights up the previously spec'd-but-unwired UiGateway service. The
// viewport is computed directly here (a settle-to-target spring) rather than
// routed through the still-stub ViewportNeuron Orleans path.
public sealed class UiGatewayService(ILogger<UiGatewayService> logger)
    : UiGateway.UiGatewayBase
{
    const string AutoLayoutElementId = "auto-layout";

    public override async Task EngageUiSession(
        IAsyncStreamReader<UiInputSynapse> requestStream,
        IServerStreamWriter<UiStateSignal> responseStream,
        ServerCallContext context)
    {
        logger.LogInformation("UI session engaged.");
        try
        {
            await foreach (var input in requestStream.ReadAllAsync(context.CancellationToken))
            {
                if (!IsAutoLayout(input)) continue;

                var target = input.Coordinates ?? new Point3D { X = 0, Y = 0, Z = 0 };
                await responseStream.WriteAsync(new UiStateSignal
                {
                    Viewport = new UiViewportSignal
                    {
                        CameraTarget = target,
                        ZoomDepth = 1.0f,
                        CameraSpring = new UiViewportSignal.Types.SpringConfig
                        {
                            DampingRatio = 0.72f,
                            NaturalFreq = 14.0f,
                        },
                        AmbientGlow = 1.0f,
                        DepthOfField = 0.0f,
                    },
                }, context.CancellationToken);
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("UI session disconnected.");
        }
    }

    static bool IsAutoLayout(UiInputSynapse input) =>
        string.Equals(input.ElementId, AutoLayoutElementId, StringComparison.OrdinalIgnoreCase);
}
