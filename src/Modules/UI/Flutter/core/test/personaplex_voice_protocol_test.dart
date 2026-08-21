import 'dart:async';
import 'dart:typed_data';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:test/test.dart';

void main() {
  test('decoded 1920-sample output preserves sequence and PCM bytes', () {
    final pcm16Bytes = Uint8List.fromList(
      List<int>.generate(3840, (index) => (index * 37 + 11) & 0xff),
    );
    final packet = Uint8List(3856);
    ByteData.sublistView(packet, 0, 16)
      ..setInt32(0, 1, Endian.little)
      ..setInt64(4, 0x0102030405060708, Endian.little)
      ..setInt32(12, 1920, Endian.little);
    packet.setRange(16, packet.length, pcm16Bytes);

    final decoded = PersonaPlexVoiceProtocol.decodeAudio(packet);

    expect(decoded.sequence, 0x0102030405060708);
    expect(decoded.pcm16Bytes, orderedEquals(pcm16Bytes));
  });

  test('voice socket URI uses the secure same-origin voice route', () {
    final socketUri = PersonaPlexVoiceClient.socketUriFor(
      Uri.parse('https://brain.example:7443/shell?ignored=yes#fragment'),
    );

    expect(socketUri, Uri.parse('wss://brain.example:7443/voice/personaplex'));
  });

  test('closing a started client closes its channel exactly once', () async {
    final channel = _FakeVoiceChannel();
    final client = PersonaPlexVoiceClient(
      baseUri: Uri.parse('http://127.0.0.1:5050'),
      channelFactory: (_) => channel,
    );

    await client.start();
    await Future.wait([client.close(), client.close()]);

    expect(channel.sent, ['{"type":"start"}', '{"type":"stop"}']);
    expect(channel.closeCount, 1);
  });

  test('channel still closes when the peer rejects the stop control', () async {
    final channel = _FakeVoiceChannel(throwOnStop: true);
    final client = PersonaPlexVoiceClient(
      baseUri: Uri.parse('http://127.0.0.1:5050'),
      channelFactory: (_) => channel,
    );

    await client.start();
    await client.close();

    expect(channel.closeCount, 1);
  });

  test('closing during connection closes the channel exactly once', () async {
    final ready = Completer<void>();
    final channel = _FakeVoiceChannel(ready: ready.future);
    final client = PersonaPlexVoiceClient(
      baseUri: Uri.parse('http://127.0.0.1:5050'),
      channelFactory: (_) => channel,
    );

    final starting = client.start();
    await Future<void>.delayed(Duration.zero);
    final closing = client.close();
    ready.complete();
    await Future.wait([starting, closing]);

    expect(channel.closeCount, 1);
  });

  test('channel closes when message subscription cancellation fails', () async {
    final channel = _FakeVoiceChannel(cancelError: StateError('cancel failed'));
    final client = PersonaPlexVoiceClient(
      baseUri: Uri.parse('http://127.0.0.1:5050'),
      channelFactory: (_) => channel,
    );

    await client.start();
    await client.close();

    expect(channel.closeCount, 1);
  });
}

final class _FakeVoiceChannel implements PersonaPlexVoiceChannel {
  _FakeVoiceChannel({
    this.throwOnStop = false,
    this.cancelError,
    Future<void>? ready,
  }) : _ready = ready ?? Future<void>.value() {
    _messages = StreamController<Object?>(
      onCancel: () {
        final error = cancelError;
        if (error != null) {
          return Future<void>.error(error);
        }
        return null;
      },
    );
  }

  final bool throwOnStop;
  final Object? cancelError;
  final Future<void> _ready;
  late final StreamController<Object?> _messages;

  final List<Object?> sent = [];
  int closeCount = 0;

  @override
  Stream<Object?> get messages => _messages.stream;

  @override
  Future<void> get ready => _ready;

  @override
  void send(Object message) {
    if (throwOnStop && message == '{"type":"stop"}') {
      throw StateError('peer closed');
    }
    sent.add(message);
  }

  @override
  Future<void> close() async {
    closeCount++;
    unawaited(_messages.close());
  }
}
