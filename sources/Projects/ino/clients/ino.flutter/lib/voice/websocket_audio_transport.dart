import 'dart:typed_data';

import 'package:web_socket_channel/web_socket_channel.dart';
import 'package:ino_flutter/voice/audio_transport.dart';

class WebSocketAudioTransport implements AudioTransport {
  WebSocketAudioTransport({required this.wsUrl});

  final String wsUrl;

  @override
  Future<String> transcribe(
    Stream<Uint8List> audioChunks, {
    int sampleRate = 16000,
    int channels = 1,
  }) async {
    final channel = WebSocketChannel.connect(Uri.parse(wsUrl));
    await channel.ready;

    await for (final chunk in audioChunks) {
      channel.sink.add(chunk);
    }
    channel.sink.close();

    final response = await channel.stream.first;
    return response as String;
  }

  @override
  Future<void> close() async {}
}
