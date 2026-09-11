import 'package:flutter/foundation.dart';
import 'package:flyer_chat_text_stream_message/flyer_chat_text_stream_message.dart';

final class StreamStateStore extends ChangeNotifier {
  final _states = <String, StreamState>{};

  StreamState stateFor(String streamId) =>
      _states[streamId] ?? StreamStateLoading();

  void start(String streamId) {
    _states[streamId] = StreamStateLoading();
    notifyListeners();
  }

  void error(String streamId, String message) {
    _states[streamId] = StreamStateError(message);
    notifyListeners();
  }

  void forget(String streamId) {
    _states.remove(streamId);
    notifyListeners();
  }
}
