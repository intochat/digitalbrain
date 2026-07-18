import 'dart:async';

import 'package:digital_brain_sdk_flutter/digital_brain_sdk_flutter.dart';
import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:google_fonts/google_fonts.dart';

import 'package:digitalbrain_flutter/app.dart';
import 'package:digitalbrain_flutter/digital_brain_ui/glow/glow_icon.dart';
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
    DigitalBrainTelemetry.initialize();
  }
  Bloc.observer = TelemetryBlocObserver();

  final perfGateway = PerfGatewayClient(
    pushSamples: (samples) => samples.drain<void>(),
    watchHints: (_) => const Stream<PerfTierHint>.empty(),
  );
  final perfStream = await PerfStream.bootstrap(gateway: perfGateway);

  runApp(
    PerfTierScope(
      notifier: perfStream.tierController,
      child: PerfProbe(stream: perfStream, child: const DigitalBrainApp()),
    ),
  );
}
