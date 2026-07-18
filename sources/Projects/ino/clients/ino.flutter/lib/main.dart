import 'dart:ui' show PlatformDispatcher;

import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter_web_plugins/url_strategy.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/app.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:ino_flutter/state/ino_bloc.dart';
import 'package:ino_flutter/state/persona_bloc.dart';
import 'package:ino_flutter/state/skills_bloc.dart';
import 'package:ino_flutter/state/timeline_bloc.dart';
import 'package:ino_flutter/state/branch_bloc.dart';
import 'package:ino_flutter/state/proposals_bloc.dart';
import 'package:ino_flutter/state/routing_bloc.dart';
import 'package:ino_flutter/state/brain_inspector_bloc.dart';
import 'package:ino_flutter/telemetry/grpc_interceptor.dart';
import 'package:ino_flutter/telemetry/bloc_observer.dart';
import 'package:ino_flutter/telemetry/telemetry.dart';
import 'package:ino_flutter/voice/audio_recorder.dart';
import 'package:ino_flutter/voice/audio_transport.dart';
import 'package:ino_flutter/voice/grpc_audio_transport.dart';
import 'package:ino_flutter/voice/websocket_audio_transport.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  usePathUrlStrategy();

  // Surface Flutter framework errors to the JS console so release-mode web
  // failures (e.g. third-party widget setup) are visible without rebuilding
  // in debug mode. Triage-only — remove after the brain three_js init lands.
  FlutterError.onError = (FlutterErrorDetails details) {
    // ignore: avoid_print
    print('[FlutterError] ${details.exceptionAsString()}\n${details.stack}');
  };
  PlatformDispatcher.instance.onError = (Object error, StackTrace stack) {
    // ignore: avoid_print
    print('[PlatformDispatcher] $error\n$stack');
    return true;
  };

  // enable accessibility semantics for E2E Playwright testing — DOM nodes appear for each widget
  if (kIsWeb && Uri.base.queryParameters.containsKey('semantics')) {
    SemanticsBinding.instance.ensureSemantics();
  }

  InoTelemetry.initialize();
  Bloc.observer = TelemetryBlocObserver();

  final (host, port) = _resolveEndpoint();
  // When served from an HTTPS origin (POC gateway runs behind Aspire's HTTPS
  // endpoint), the gRPC-Web channel must also use TLS — otherwise grpc-dart
  // rewrites the request URL to http:// and the browser aborts with a mixed
  // content / empty-response error.
  final isHttps = kIsWeb && Uri.base.scheme == 'https';

  final client = InoGrpcClient(
    host: host,
    port: port,
    transportSecure: isHttps,
    interceptors: [OtelGrpcInterceptor()],
  );

  final wsScheme = isHttps ? 'wss' : 'ws';
  final AudioTransport audioTransport = kIsWeb
      ? WebSocketAudioTransport(wsUrl: '$wsScheme://$host:$port/ws/audio')
      : GrpcAudioTransport(channel: client.channel);
  final recorder = AudioRecorderService();

  final personaBloc = PersonaBloc(client: client);
  final timelineBloc = TimelineBloc(client: client);

  // Start the live tail at app boot so events fired from the Chat screen
  // stream into the bloc's buffer before the user ever opens /timeline. Without
  // this, the subscription only opens on first /timeline visit and silently
  // drops any events fired before that.
  timelineBloc.add(TimelineStarted());

  // forward only newly-appended timeline entries to persona
  var lastSeenCount = 0;
  timelineBloc.stream.listen((timelineState) {
    final events = timelineState.events;
    if (events.length > lastSeenCount) {
      for (var i = lastSeenCount; i < events.length; i++) {
        personaBloc.add(PersonaTimelineEvent(events[i].source, events[i].kind, verb: events[i].verb));
      }
      lastSeenCount = events.length;
    }
  });

  runApp(
    MultiBlocProvider(
      providers: [
        BlocProvider(
          create: (_) => InoBloc(
            client: client,
            audioTransport: audioTransport,
            recorder: recorder,
          ),
        ),
        BlocProvider.value(value: personaBloc),
        BlocProvider(create: (_) => SkillsBloc(client: client)),
        BlocProvider.value(value: timelineBloc),
        BlocProvider(create: (_) => BranchBloc(client: client)),
        BlocProvider(
          create: (_) => ProposalsBloc(client: client),
          lazy: false,
        ),
        BlocProvider(
          create: (_) => RoutingBloc(client: client),
          lazy: false,
        ),
        BlocProvider(create: (_) => BrainInspectorBloc(), lazy: false),
      ],
      child: const InoApp(),
    ),
  );
}

(String, int) _resolveEndpoint() {
  if (kIsWeb) {
    final uri = Uri.base;
    return (uri.host, uri.port);
  }
  const envHost =
      String.fromEnvironment('GRPC_HOST', defaultValue: 'localhost');
  const envPort = int.fromEnvironment('GRPC_PORT', defaultValue: 5400);
  return (envHost, envPort);
}
