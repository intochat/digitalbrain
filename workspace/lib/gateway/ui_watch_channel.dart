import 'dart:async';

import 'package:web_socket_channel/web_socket_channel.dart';

abstract class UiWatchChannel {
  Stream<dynamic> get stream;
  Future<void> get ready;
  Future<void> close();
}

typedef UiWatchChannelFactory = Future<UiWatchChannel> Function(Uri uri);

class WebSocketUiWatchChannel implements UiWatchChannel {
  WebSocketUiWatchChannel(this._channel);

  final WebSocketChannel _channel;

  @override
  Stream<dynamic> get stream => _channel.stream;

  @override
  Future<void> get ready => _channel.ready;

  @override
  Future<void> close() => _channel.sink.close();
}

Future<UiWatchChannel> defaultWatchChannelFactory(Uri uri) async {
  final channel = WebSocketChannel.connect(uri);
  return WebSocketUiWatchChannel(channel);
}
