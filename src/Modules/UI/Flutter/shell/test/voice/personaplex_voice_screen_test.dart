import 'dart:async';
import 'dart:typed_data';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/voice/pcm_audio_output.dart';
import 'package:digitalbrain_flutter_shell/voice/personaplex_voice_controller.dart';
import 'package:digitalbrain_flutter_shell/voice/personaplex_voice_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_soloud/flutter_soloud.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:record/record.dart';

void main() {
  testWidgets(
    'deactivation during retry release cannot create a late voice session',
    (tester) async {
      final active = ValueNotifier(true);
      final release = Completer<void>();
      final sessions = <_ScreenSession>[];
      final first = _ScreenSession(
        permission: false,
        captureStopGate: release.future,
      );

      await tester.pumpWidget(
        _voiceHost(
          active: active,
          controllerFactory: () {
            final session = sessions.isEmpty ? first : _ScreenSession();
            sessions.add(session);
            return session.controller;
          },
        ),
      );
      await _tapStart(tester);
      await tester.pumpAndSettle();
      expect(find.textContaining('permission denied'), findsOneWidget);

      await _tapStart(tester);
      await tester.pump();
      active.value = false;
      await tester.pump();
      release.complete();
      await tester.pumpAndSettle();

      expect(sessions, hasLength(1));
      expect(first.capture.stopCount, 1);
      expect(first.capture.disposeCount, 1);
      expect(first.output.stopCount, 1);
      expect(first.output.disposeCount, 1);
      expect(first.transport.closeCount, 1);

      active.dispose();
      await tester.pumpWidget(const SizedBox.shrink());
      await tester.pumpAndSettle();
    },
  );

  testWidgets('double Start leaves one session and disposes every controller', (
    tester,
  ) async {
    final active = ValueNotifier(true);
    final sessions = <_ScreenSession>[];

    await tester.pumpWidget(
      _voiceHost(
        active: active,
        controllerFactory: () {
          final session = _ScreenSession();
          sessions.add(session);
          return session.controller;
        },
      ),
    );

    final startButton = tester.widget<FilledButton>(
      find.byKey(const Key('personaplex_voice_start')),
    );
    startButton.onPressed!();
    startButton.onPressed!();
    await tester.pumpAndSettle();

    expect(sessions, hasLength(1));
    expect(sessions.single.capture.startCount, 1);
    expect(sessions.single.output.startCount, 1);
    expect(sessions.single.transport.startCount, 1);

    active.dispose();
    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pumpAndSettle();
    expect(sessions.single.capture.stopCount, 1);
    expect(sessions.single.output.stopCount, 1);
    expect(sessions.single.transport.closeCount, 1);
    await tester.pump();

    for (final session in sessions) {
      expect(
        [
          session.capture.stopCount,
          session.output.stopCount,
          session.transport.closeCount,
          session.capture.disposeCount,
          session.output.disposeCount,
        ],
        [1, 1, 1, 1, 1],
      );
    }
  });
}

Future<void> _tapStart(WidgetTester tester) async {
  final start = find.byKey(const Key('personaplex_voice_start'));
  await tester.ensureVisible(start);
  await tester.pumpAndSettle();
  await tester.tap(start);
}

Widget _voiceHost({
  required ValueNotifier<bool> active,
  required PersonaPlexVoiceControllerFactory controllerFactory,
}) {
  return MaterialApp(
    home: ValueListenableBuilder<bool>(
      valueListenable: active,
      builder: (context, isActive, child) => PersonaPlexVoiceScreen(
        active: isActive,
        controllerFactory: controllerFactory,
      ),
    ),
  );
}

final class _ScreenSession {
  _ScreenSession({bool permission = true, Future<void>? captureStopGate})
    : capture = _ScreenCapture(
        permission: permission,
        stopGate: captureStopGate,
      ),
      output = _ScreenOutput(),
      transport = _ScreenTransport() {
    controller = PersonaPlexVoiceController(
      capture: capture,
      output: output,
      transport: transport,
    );
  }

  final _ScreenCapture capture;
  final _ScreenOutput output;
  final _ScreenTransport transport;
  late final PersonaPlexVoiceController controller;
}

final class _ScreenCapture implements PersonaPlexAudioCapture {
  _ScreenCapture({required this.permission, this.stopGate});

  final bool permission;
  final Future<void>? stopGate;
  final _pcm = _ManualStream<Uint8List>();
  int startCount = 0;
  int stopCount = 0;
  int disposeCount = 0;

  @override
  Future<bool> hasPermission() async => permission;

  @override
  Future<bool> isPcm16Supported() async => true;

  @override
  Future<List<InputDevice>> listInputDevices() async => const [];

  @override
  Future<Stream<Uint8List>> start({InputDevice? device}) async {
    startCount++;
    return _pcm;
  }

  @override
  Future<Stream<Uint8List>> restart({InputDevice? device}) =>
      start(device: device);

  @override
  Future<void> stop() async {
    stopCount++;
    final gate = stopGate;
    if (gate != null) {
      await gate;
    }
  }

  @override
  Future<void> dispose() async {
    disposeCount++;
  }
}

final class _ScreenOutput implements PcmAudioOutput {
  int startCount = 0;
  int stopCount = 0;
  int disposeCount = 0;

  @override
  List<PlaybackDevice> listPlaybackDevices() => const [];

  @override
  Future<void> start({PlaybackDevice? device}) async {
    startCount++;
  }

  @override
  Future<void> setPlaybackDevice(PlaybackDevice device) async {}

  @override
  Future<void> addPcm16(Uint8List pcm16Bytes) async {}

  @override
  Future<void> stop() async {
    stopCount++;
  }

  @override
  Future<void> dispose() async {
    disposeCount++;
  }
}

final class _ScreenTransport implements PersonaPlexVoiceTransport {
  final _events = _ManualStream<PersonaPlexVoiceEvent>();
  int startCount = 0;
  int closeCount = 0;

  @override
  Stream<PersonaPlexVoiceEvent> get events => _events;

  @override
  Future<void> start() async {
    startCount++;
    _events.emit(
      const PersonaPlexVoiceStatus(
        state: 'ready',
        message: 'PersonaPlex session is ready.',
      ),
    );
  }

  @override
  void sendAudio({required int sequence, required Uint8List pcm16Bytes}) {}

  @override
  Future<void> close() async {
    closeCount++;
  }
}

final class _ManualStream<T> extends Stream<T> {
  final _subscriptions = <_ManualSubscription<T>>[];

  void emit(T event) {
    for (final subscription in List.of(_subscriptions)) {
      subscription.emit(event);
    }
  }

  @override
  StreamSubscription<T> listen(
    void Function(T event)? onData, {
    Function? onError,
    void Function()? onDone,
    bool? cancelOnError,
  }) {
    final subscription = _ManualSubscription<T>(this, onData);
    _subscriptions.add(subscription);
    return subscription;
  }
}

final class _ManualSubscription<T> implements StreamSubscription<T> {
  _ManualSubscription(this._owner, this._onData);

  final _ManualStream<T> _owner;
  final void Function(T event)? _onData;
  bool _cancelled = false;

  void emit(T event) {
    if (!_cancelled) {
      _onData?.call(event);
    }
  }

  @override
  Future<void> cancel() async {
    _cancelled = true;
    _owner._subscriptions.remove(this);
  }

  @override
  dynamic noSuchMethod(Invocation invocation) => throw UnsupportedError(
    '${invocation.memberName} is not used by this test.',
  );
}
