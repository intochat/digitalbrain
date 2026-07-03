import 'package:digital_brain_sdk_flutter/digital_brain_sdk_flutter.dart';
import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:media_kit/media_kit.dart';

import 'package:digitalbrain_flutter/app.dart';
import 'package:digitalbrain_flutter/digital_brain_ui/glow/glow_icon.dart';
import 'package:digitalbrain_flutter/features/surface_demo/surface_demo_screen.dart';
import 'package:digitalbrain_flutter/grpc/digitalbrain.pb.dart' as gw;
import 'package:digitalbrain_flutter/grpc/digitalbrain.pbgrpc.dart';
import 'package:digitalbrain_flutter/grpc/endpoint.dart';
import 'package:digitalbrain_flutter/grpc/grpc_channel.dart';
import 'package:digitalbrain_flutter/telemetry/bloc_observer.dart';
import 'package:digitalbrain_flutter/telemetry/telemetry.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  if (kIsWeb && const bool.fromEnvironment('DIGITALBRAIN_E2E')) {
    SemanticsBinding.instance.ensureSemantics();
  }
  GoogleFonts.config.allowRuntimeFetching = true;
  WidgetCensus.glowIconType = GlowIcon;
  if (!kIsWeb) {
    MediaKit.ensureInitialized();
  }

  DigitalBrainTelemetry.initialize();
  Bloc.observer = TelemetryBlocObserver();

  if (const bool.fromEnvironment('SURFACE_DEMO')) {
    runApp(const SurfaceDemoApp());
    return;
  }

  final (host, port, secure) = resolveKernelEndpoint();
  DigitalBrainTelemetry.instance.logger.info(
    'kernel endpoint resolved',
    attrs: {'host': host, 'port': port.toString(), 'secure': secure.toString()},
  );

  // Dedicated perf-only gRPC client. Isolated from the per-bloc gRPC client
  // each feature constructs, so a perf-stream disconnect can't cascade.
  final perfChannel = createKernelChannel(
    host: host,
    port: port,
    secure: secure,
  );
  final perfGatewayStub = DigitalBrainGatewayClient(perfChannel);
  final perfGateway = PerfGatewayClient(
    pushSamples: (Stream<PerfSample> samples) async {
      final protoStream = samples.map(
        (s) => gw.FlutterPerfSampleProto(
          clientId: s.clientId,
          sampleWindowId: s.sampleWindowId,
          frameCount: s.frameCount,
          p50FrameMs: s.p50FrameMs,
          p95FrameMs: s.p95FrameMs,
          jankPct: s.jankPct,
          widgetCount: s.widgetCount,
          glowPainterCount: s.glowPainterCount,
          rebuildsPerSecond: s.rebuildsPerSecond,
          platform: s.platform,
          timestamp: s.timestamp.toIso8601String(),
        ),
      );
      await perfGatewayStub.pushFlutterPerf(protoStream);
    },
    watchHints: (String clientId) => perfGatewayStub
        .watchVisualLoadHint(gw.WatchVisualLoadHintRequest(clientId: clientId))
        .map(
          (proto) => PerfTierHint(
            tier: perfTierFromString(proto.tier),
            reason: proto.reason,
          ),
        ),
  );
  final perfStream = await PerfStream.bootstrap(gateway: perfGateway);

  runApp(
    PerfTierScope(
      notifier: perfStream.tierController,
      child: PerfProbe(stream: perfStream, child: const DigitalBrainApp()),
    ),
  );
}
