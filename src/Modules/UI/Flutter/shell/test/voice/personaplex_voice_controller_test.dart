import 'dart:async';
import 'dart:typed_data';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/voice/pcm_audio_output.dart';
import 'package:digitalbrain_flutter_shell/voice/personaplex_voice_controller.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('stopping a session closes capture playback and socket once', () async {
    final capture = _FakeCapture();
    final output = _FakeOutput();
    final transport = _FakeTransport();
    final controller = PersonaPlexVoiceController(
      capture: capture,
      output: output,
      transport: transport,
    );

    await controller.start();
    await Future.wait([controller.stop(), controller.stop()]);
    await controller.disposeAsync();

    expect(capture.stopCount, 1);
    expect(output.stopCount, 1);
    expect(transport.closeCount, 1);
  });

  test('capture chunks are sent as exact ordered PersonaPlex frames', () async {
    final capture = _FakeCapture();
    final output = _FakeOutput();
    final transport = _FakeTransport(readyOnStart: true);
    final controller = PersonaPlexVoiceController(
      capture: capture,
      output: output,
      transport: transport,
    );
    addTearDown(controller.disposeAsync);

    await controller.start();
    await Future<void>.delayed(Duration.zero);
    capture.add(Uint8List.fromList(List<int>.generate(997, (i) => i & 0xff)));
    capture.add(
      Uint8List.fromList(List<int>.generate(6693, (i) => (i + 17) & 0xff)),
    );
    await Future<void>.delayed(Duration.zero);

    expect(transport.audio.length, 2);
    expect(transport.audio.map((frame) => frame.sequence), [1, 2]);
    expect(transport.audio.map((frame) => frame.pcm16Bytes.length), [
      3840,
      3840,
    ]);
  });

  test('assistant PCM is played and updates output metrics', () async {
    final capture = _FakeCapture();
    final output = _FakeOutput();
    final transport = _FakeTransport(readyOnStart: true);
    final controller = PersonaPlexVoiceController(
      capture: capture,
      output: output,
      transport: transport,
    );
    addTearDown(controller.disposeAsync);

    await controller.start();
    await Future<void>.delayed(Duration.zero);
    final pcm = Uint8List(3840);
    for (var offset = 0; offset < pcm.length; offset += 2) {
      ByteData.sublistView(
        pcm,
        offset,
        offset + 2,
      ).setInt16(0, 12000, Endian.little);
    }
    capture.add(pcm);
    await Future<void>.delayed(Duration.zero);
    transport.emit(
      PersonaPlexVoiceAudio(
        PersonaPlexAudioPacket(sequence: 1, pcm16Bytes: pcm),
      ),
    );
    await Future<void>.delayed(Duration.zero);

    expect(output.added, hasLength(1));
    expect(output.added.single, orderedEquals(pcm));
    expect(controller.microphoneLevel, greaterThan(0));
    expect(controller.speakerLevel, greaterThan(0));
    expect(controller.latencyMilliseconds, isNotNull);
  });

  test('permission denial does not open playback or a socket', () async {
    final capture = _FakeCapture(permission: false);
    final output = _FakeOutput();
    final transport = _FakeTransport();
    final controller = PersonaPlexVoiceController(
      capture: capture,
      output: output,
      transport: transport,
    );
    addTearDown(controller.disposeAsync);

    await controller.start();

    expect(controller.phase, PersonaPlexVoicePhase.permissionDenied);
    expect(output.startCount, 0);
    expect(transport.startCount, 0);
  });

  test('stopping during permission check prevents late startup', () async {
    final permission = Completer<bool>();
    final capture = _FakeCapture(permissionResult: permission.future);
    final output = _FakeOutput();
    final transport = _FakeTransport();
    final controller = PersonaPlexVoiceController(
      capture: capture,
      output: output,
      transport: transport,
    );
    addTearDown(controller.disposeAsync);

    final starting = controller.start();
    await Future<void>.delayed(Duration.zero);
    final stopping = controller.stop();
    permission.complete(true);
    await Future.wait([starting, stopping]);

    expect(controller.phase, PersonaPlexVoicePhase.stopped);
    expect(output.startCount, 0);
    expect(transport.startCount, 0);
  });

  test('adjusted capture format becomes explicitly unavailable', () async {
    final capture = _FakeCapture();
    final output = _FakeOutput();
    final transport = _FakeTransport(readyOnStart: true);
    final controller = PersonaPlexVoiceController(
      capture: capture,
      output: output,
      transport: transport,
    );
    addTearDown(controller.disposeAsync);

    await controller.start();
    await Future<void>.delayed(Duration.zero);
    capture.addError(
      const PersonaPlexVoiceUnavailableException(
        'Microphone adjusted capture away from 24 kHz mono PCM16.',
      ),
    );
    await Future<void>.delayed(Duration.zero);

    expect(controller.phase, PersonaPlexVoicePhase.unavailable);
    expect(controller.statusMessage, contains('24 kHz mono PCM16'));
  });

  test('socket send failure enters error state and stops resources', () async {
    final capture = _FakeCapture();
    final output = _FakeOutput();
    final transport = _FakeTransport(
      readyOnStart: true,
      sendError: StateError('socket closed'),
    );
    final controller = PersonaPlexVoiceController(
      capture: capture,
      output: output,
      transport: transport,
    );
    addTearDown(controller.disposeAsync);

    await controller.start();
    await Future<void>.delayed(Duration.zero);
    capture.add(Uint8List(PersonaPlexVoiceProtocol.pcmByteCount));
    await Future<void>.delayed(Duration.zero);

    expect(controller.phase, PersonaPlexVoicePhase.error);
    expect(capture.stopCount, 1);
    expect(output.stopCount, 1);
    expect(transport.closeCount, 1);
  });

  test(
    'continuous output streams 24 kHz mono PCM16 and releases once',
    () async {
      final engine = _FakePcmAudioEngine();
      final output = SoLoudPcmAudioOutput(engine: engine);
      final first = Uint8List(3840);
      final second = Uint8List.fromList(List<int>.filled(3840, 7));

      await output.start();
      await output.addPcm16(first);
      await output.addPcm16(second);
      await Future.wait([output.stop(), output.stop()]);
      await output.dispose();

      expect(engine.sampleRate, 24000);
      expect(engine.channelCount, 1);
      expect(engine.added, [orderedEquals(first), orderedEquals(second)]);
      expect(engine.playCount, 1);
      expect(engine.releaseCount, 1);
      expect(engine.deinitializeCount, 1);
    },
  );

  test(
    'stopping during output initialization cannot create a late stream',
    () async {
      final initialization = Completer<void>();
      final engine = _FakePcmAudioEngine(initialization: initialization.future);
      final output = SoLoudPcmAudioOutput(engine: engine);

      final starting = output.start();
      await Future<void>.delayed(Duration.zero);
      final stopping = output.stop();
      initialization.complete();
      await Future.wait([starting, stopping]);

      expect(engine.createCount, 0);
      expect(engine.deinitializeCount, 1);
    },
  );
}

final class _FakeCapture implements PersonaPlexAudioCapture {
  _FakeCapture({bool permission = true, Future<bool>? permissionResult})
    : _permission = permissionResult ?? Future<bool>.value(permission);

  final Future<bool> _permission;
  final _pcm = StreamController<Uint8List>();
  bool started = false;
  int stopCount = 0;

  @override
  Future<bool> hasPermission() => _permission;

  @override
  Future<bool> isPcm16Supported() async => true;

  @override
  Future<Stream<Uint8List>> start() async {
    started = true;
    return _pcm.stream;
  }

  void add(Uint8List bytes) => _pcm.add(bytes);

  void addError(Object error) => _pcm.addError(error);

  @override
  Future<void> stop() async {
    stopCount++;
    if (started) {
      await _pcm.close();
    }
  }

  @override
  Future<void> dispose() async {}
}

final class _FakeOutput implements PcmAudioOutput {
  final List<Uint8List> added = [];
  int startCount = 0;
  int stopCount = 0;

  @override
  Future<void> start() async {
    startCount++;
  }

  @override
  Future<void> addPcm16(Uint8List pcm16Bytes) async {
    added.add(Uint8List.fromList(pcm16Bytes));
  }

  @override
  Future<void> stop() async {
    stopCount++;
  }

  @override
  Future<void> dispose() async {}
}

final class _FakeTransport implements PersonaPlexVoiceTransport {
  _FakeTransport({this.readyOnStart = false, this.sendError});

  final bool readyOnStart;
  final Object? sendError;
  final _events = StreamController<PersonaPlexVoiceEvent>.broadcast();
  final List<PersonaPlexAudioPacket> audio = [];
  int startCount = 0;
  int closeCount = 0;

  @override
  Stream<PersonaPlexVoiceEvent> get events => _events.stream;

  @override
  Future<void> start() async {
    startCount++;
    if (readyOnStart) {
      _events.add(
        const PersonaPlexVoiceStatus(
          state: 'ready',
          message: 'PersonaPlex session is ready.',
        ),
      );
    }
  }

  @override
  void sendAudio({required int sequence, required Uint8List pcm16Bytes}) {
    if (sendError case final error?) {
      throw error;
    }
    audio.add(
      PersonaPlexAudioPacket(sequence: sequence, pcm16Bytes: pcm16Bytes),
    );
  }

  void emit(PersonaPlexVoiceEvent event) => _events.add(event);

  @override
  Future<void> close() async {
    closeCount++;
    await _events.close();
  }
}

final class _FakePcmAudioEngine implements PcmAudioEngine {
  _FakePcmAudioEngine({Future<void>? initialization})
    : _initialization = initialization ?? Future<void>.value();

  final Future<void> _initialization;
  final Object stream = Object();
  final List<Uint8List> added = [];
  int? sampleRate;
  int? channelCount;
  int playCount = 0;
  int createCount = 0;
  int releaseCount = 0;
  int deinitializeCount = 0;

  @override
  Future<void> initialize({
    required int sampleRate,
    required int channelCount,
  }) async {
    this.sampleRate = sampleRate;
    this.channelCount = channelCount;
    await _initialization;
  }

  @override
  Object createPcm16Stream() {
    createCount++;
    return stream;
  }

  @override
  void addPcm16(Object stream, Uint8List pcm16Bytes) {
    expect(stream, same(this.stream));
    added.add(Uint8List.fromList(pcm16Bytes));
  }

  @override
  Future<void> play(Object stream) async {
    expect(stream, same(this.stream));
    playCount++;
  }

  @override
  Future<void> release(Object stream) async {
    expect(stream, same(this.stream));
    releaseCount++;
  }

  @override
  Future<void> deinitialize() async {
    deinitializeCount++;
  }
}
