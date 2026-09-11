import 'dart:convert';

import 'ui_models.dart';
import 'sse_frames.dart';

final class SseChatTurnParser extends SseFrameParser<ChatStreamEvent> {
  @override
  Iterable<ChatStreamEvent> decode(String eventName, String data) sync* {
    if (eventName != 'chat-turn' && eventName != 'reset') {
      return;
    }

    // Malformed known events are broken contracts.
    final json = jsonDecode(data) as Map<String, dynamic>;
    if (eventName == 'chat-turn') {
      yield ChatTurnObserved(turn: ChatTurnEvent.fromJson(json));
      return;
    }

    yield ChatJournalReset(
      cursor: (json['cursor'] as num).toInt(),
      turns: ChatTurnEvent.replayTurns((json['state'] as Map)['turns'] as List),
    );
  }
}
