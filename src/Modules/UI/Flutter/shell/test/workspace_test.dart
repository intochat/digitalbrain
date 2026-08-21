import 'dart:async';
import 'dart:typed_data';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_soloud/flutter_soloud.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:record/record.dart';

import 'support/shell_test_support.dart';

void main() {
  testWidgets(
    'the workspace exposes Chat, Voice, Activity, Behaviors, Kit, and Windowing destinations',
    (tester) async {
      await prepareShellSurface(tester);

      await tester.pumpWidget(const BrainChatApp(chatName: 'main'));
      await tester.pumpAndSettle();
      await drainShellTimers(tester);

      expect(find.byKey(const Key('destination_chat')), findsOneWidget);
      expect(find.byKey(const Key('destination_voice')), findsOneWidget);
      expect(find.byKey(const Key('destination_activity')), findsOneWidget);
      expect(find.byKey(const Key('destination_behaviors')), findsOneWidget);
      expect(find.byKey(const Key('destination_kit')), findsOneWidget);
      expect(find.byKey(const Key('destination_windowing')), findsOneWidget);

      await tester.tap(find.byKey(const Key('destination_voice')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('personaplex_voice_screen')), findsOneWidget);
      expect(find.textContaining('unavailable'), findsWidgets);

      await tester.tap(find.byKey(const Key('destination_activity')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('activity_screen')), findsOneWidget);
      expect(find.text('No activity yet.'), findsOneWidget);

      await tester.tap(find.byKey(const Key('destination_behaviors')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('behavior_workspace')), findsOneWidget);
      expect(find.text('Behavior recipes'), findsOneWidget);
      expect(
        find.textContaining('Google Calendar', findRichText: true),
        findsOneWidget,
      );
      expect(
        find.textContaining('ICalendar', findRichText: true),
        findsWidgets,
      );
      expect(find.text('Planned composition'), findsOneWidget);
      await drainShellTimers(tester);

      await tester.tap(find.byKey(const Key('destination_kit')));
      await tester.pump();
      await drainShellTimers(tester);
      expect(find.byKey(const Key('kit_gallery_screen')), findsOneWidget);
      expect(find.text('UI Kit'), findsOneWidget);

      await tester.tap(find.byKey(const Key('destination_windowing')));
      await tester.pump();
      await drainShellTimers(tester);
      expect(find.byKey(const Key('windowing_screen')), findsOneWidget);
      expect(find.textContaining('Windowing demo'), findsOneWidget);
      expect(find.text('BTC / USD'), findsWidgets);
      expect(find.byKey(const Key('kit_time_chart')), findsOneWidget);
    },
  );

  testWidgets('switching away from Voice stops all native session resources', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    final capture = _WorkspaceCapture();
    final output = _WorkspaceOutput();
    final transport = _WorkspaceTransport();
    final controller = PersonaPlexVoiceController(
      capture: capture,
      output: output,
      transport: transport,
    );

    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        personaPlexVoiceControllerFactory: () => controller,
      ),
    );
    await tester.tap(find.byKey(const Key('destination_voice')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('personaplex_voice_start')));
    await tester.pumpAndSettle();

    expect(find.text('PersonaPlex is listening and speaking.'), findsOneWidget);
    expect(
      find.byKey(const Key('personaplex_microphone_level')),
      findsOneWidget,
    );
    expect(find.byKey(const Key('personaplex_speaker_level')), findsOneWidget);
    expect(find.byKey(const Key('personaplex_voice_stop')), findsOneWidget);
    expect(find.textContaining('Latency'), findsOneWidget);

    await tester.tap(find.byKey(const Key('destination_chat')));
    await tester.pumpAndSettle();

    final hiddenVoice = tester.widget<PersonaPlexVoiceScreen>(
      find.byType(PersonaPlexVoiceScreen, skipOffstage: false),
    );
    expect(hiddenVoice.active, isFalse);
    expect(capture.stopCount, 1);
    expect(output.stopCount, 1);
    expect(transport.closeCount, 1);
  });

  testWidgets('narrow windows use bottom navigation', (tester) async {
    tester.view.physicalSize = const Size(600, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(const BrainChatApp(chatName: 'main'));
    await tester.pumpAndSettle();
    await drainShellTimers(tester);

    expect(find.byType(NavigationBar), findsOneWidget);
    expect(find.byType(NavigationRail), findsNothing);
    await drainShellTimers(tester);
  });

  testWidgets(
    'a disconnected edge says so and mounts chat without a send path',
    (tester) async {
      await prepareShellSurface(tester);
      await tester.pumpWidget(
        const BrainChatApp(chatName: 'main', statusMessage: 'no edge'),
      );
      await tester.pump();

      expect(find.text('not connected'), findsOneWidget);
      expect(find.byKey(const Key('chat_surface')), findsOneWidget);
      await drainShellTimers(tester);
    },
  );
}

final class _WorkspaceCapture implements PersonaPlexAudioCapture {
  final _pcm = StreamController<Uint8List>();
  int stopCount = 0;

  @override
  Future<bool> hasPermission() async => true;

  @override
  Future<bool> isPcm16Supported() async => true;

  @override
  Future<List<InputDevice>> listInputDevices() async => const [];

  @override
  Future<Stream<Uint8List>> start({InputDevice? device}) async => _pcm.stream;

  @override
  Future<Stream<Uint8List>> restart({InputDevice? device}) =>
      start(device: device);

  @override
  Future<void> stop() async {
    stopCount++;
    await _pcm.close();
  }

  @override
  Future<void> dispose() async {}
}

final class _WorkspaceOutput implements PcmAudioOutput {
  int stopCount = 0;

  @override
  List<PlaybackDevice> listPlaybackDevices() => const [];

  @override
  Future<void> start({PlaybackDevice? device}) async {}

  @override
  Future<void> setPlaybackDevice(PlaybackDevice device) async {}

  @override
  Future<void> addPcm16(Uint8List pcm16Bytes) async {}

  @override
  Future<void> stop() async {
    stopCount++;
  }

  @override
  Future<void> dispose() async {}
}

final class _WorkspaceTransport implements PersonaPlexVoiceTransport {
  final _events = StreamController<PersonaPlexVoiceEvent>.broadcast();
  int closeCount = 0;

  @override
  Stream<PersonaPlexVoiceEvent> get events => _events.stream;

  @override
  Future<void> start() async {
    _events.add(
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
    await _events.close();
  }
}
