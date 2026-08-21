import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:web_socket_channel/web_socket_channel.dart';

import 'personaplex_voice_protocol.dart';

sealed class PersonaPlexVoiceEvent {
  const PersonaPlexVoiceEvent();
}

final class PersonaPlexVoiceStatus extends PersonaPlexVoiceEvent {
  const PersonaPlexVoiceStatus({required this.state, required this.message});

  final String state;
  final String message;
}

final class PersonaPlexVoiceAudio extends PersonaPlexVoiceEvent {
  const PersonaPlexVoiceAudio(this.packet);

  final PersonaPlexAudioPacket packet;
}

final class PersonaPlexVoiceError extends PersonaPlexVoiceEvent {
  const PersonaPlexVoiceError({required this.code, required this.message});

  final String code;
  final String message;
}

final class PersonaPlexVoiceStopped extends PersonaPlexVoiceEvent {
  const PersonaPlexVoiceStopped();
}

abstract interface class PersonaPlexVoiceChannel {
  Future<void> get ready;
  Stream<Object?> get messages;
  void send(Object message);
  Future<void> close();
}

typedef PersonaPlexVoiceChannelFactory =
    PersonaPlexVoiceChannel Function(Uri socketUri);

abstract interface class PersonaPlexVoiceTransport {
  Stream<PersonaPlexVoiceEvent> get events;
  Future<void> start();
  void sendAudio({required int sequence, required Uint8List pcm16Bytes});
  Future<void> close();
}

final class PersonaPlexVoiceClient implements PersonaPlexVoiceTransport {
  PersonaPlexVoiceClient({
    required Uri baseUri,
    PersonaPlexVoiceChannelFactory? channelFactory,
  }) : socketUri = socketUriFor(baseUri),
       _channelFactory = channelFactory ?? _WebSocketVoiceChannel.new;

  final Uri socketUri;
  final PersonaPlexVoiceChannelFactory _channelFactory;
  final StreamController<PersonaPlexVoiceEvent> _events =
      StreamController<PersonaPlexVoiceEvent>.broadcast(sync: true);

  PersonaPlexVoiceChannel? _channel;
  StreamSubscription<Object?>? _subscription;
  Future<void>? _closeFuture;
  bool _startSent = false;
  bool _closed = false;

  @override
  Stream<PersonaPlexVoiceEvent> get events => _events.stream;

  static Uri socketUriFor(Uri baseUri) {
    final scheme = switch (baseUri.scheme.toLowerCase()) {
      'http' => 'ws',
      'https' => 'wss',
      'ws' => 'ws',
      'wss' => 'wss',
      _ => throw ArgumentError.value(
        baseUri,
        'baseUri',
        'must use http, https, ws, or wss',
      ),
    };
    return Uri(
      scheme: scheme,
      userInfo: baseUri.userInfo,
      host: baseUri.host,
      port: baseUri.hasPort ? baseUri.port : null,
      path: '/voice/personaplex',
    );
  }

  @override
  Future<void> start() async {
    if (_closed) {
      throw StateError('PersonaPlex voice client is closed.');
    }
    if (_channel != null) {
      return;
    }

    final channel = _channelFactory(socketUri);
    _channel = channel;
    await channel.ready;
    if (_closed) {
      return;
    }

    _subscription = channel.messages.listen(
      _handleMessage,
      onError: _handleChannelError,
      onDone: _handleChannelDone,
    );
    channel.send(jsonEncode(const {'type': 'start'}));
    _startSent = true;
  }

  @override
  void sendAudio({required int sequence, required Uint8List pcm16Bytes}) {
    if (!_startSent || _closed || _channel == null) {
      throw StateError('PersonaPlex voice client is not active.');
    }
    _channel!.send(
      PersonaPlexVoiceProtocol.encodeAudio(
        sequence: sequence,
        pcm16Bytes: pcm16Bytes,
      ),
    );
  }

  @override
  Future<void> close() => _closeFuture ??= _close();

  Future<void> _close() async {
    _closed = true;
    final subscription = _subscription;
    _subscription = null;
    await subscription?.cancel();

    final channel = _channel;
    _channel = null;
    try {
      if (channel != null) {
        if (_startSent) {
          try {
            channel.send(jsonEncode(const {'type': 'stop'}));
          } on Object {
            // The peer may already be gone; closing the channel still matters.
          }
          _startSent = false;
        }
        await channel.close();
      }
    } finally {
      if (!_events.isClosed) {
        await _events.close();
      }
    }
  }

  void _handleMessage(Object? message) {
    try {
      if (message case final List<int> bytes) {
        _events.add(
          PersonaPlexVoiceAudio(
            PersonaPlexVoiceProtocol.decodeAudio(Uint8List.fromList(bytes)),
          ),
        );
        return;
      }
      if (message is! String) {
        throw const FormatException(
          'PersonaPlex WebSocket messages must be text or binary.',
        );
      }

      final json = jsonDecode(message);
      if (json is! Map<String, dynamic>) {
        throw const FormatException(
          'PersonaPlex control messages must be JSON objects.',
        );
      }
      switch (json['type']) {
        case 'status':
          _events.add(
            PersonaPlexVoiceStatus(
              state: _requiredString(json, 'state'),
              message: _requiredString(json, 'message'),
            ),
          );
        case 'error':
          _events.add(
            PersonaPlexVoiceError(
              code: _requiredString(json, 'code'),
              message: _requiredString(json, 'message'),
            ),
          );
        case 'stop':
          _events.add(const PersonaPlexVoiceStopped());
        default:
          throw const FormatException(
            'Unsupported PersonaPlex control message type.',
          );
      }
    } on Object catch (error, stackTrace) {
      _events.addError(error, stackTrace);
    }
  }

  void _handleChannelError(Object error, StackTrace stackTrace) {
    if (!_events.isClosed) {
      _events.addError(error, stackTrace);
    }
  }

  void _handleChannelDone() {
    if (!_events.isClosed) {
      _events.add(const PersonaPlexVoiceStopped());
    }
  }

  static String _requiredString(Map<String, dynamic> json, String key) {
    final value = json[key];
    if (value is! String || value.isEmpty) {
      throw FormatException('PersonaPlex control message requires $key.');
    }
    return value;
  }
}

final class _WebSocketVoiceChannel implements PersonaPlexVoiceChannel {
  _WebSocketVoiceChannel(Uri socketUri)
    : _channel = WebSocketChannel.connect(socketUri);

  final WebSocketChannel _channel;

  @override
  Future<void> get ready => _channel.ready;

  @override
  Stream<Object?> get messages => _channel.stream;

  @override
  void send(Object message) => _channel.sink.add(message);

  @override
  Future<void> close() async {
    await _channel.sink.close(1000, 'PersonaPlex voice session ended.');
  }
}
