import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/foundation.dart';

import 'chat_contracts.dart';

/// Stream subscriptions and projected state for [BrainWorkspace].
///
/// Keeps durable chat session state out of the widget tree so the shell
/// can rebuild chrome without re-owning SSE bookkeeping.
final class WorkspaceSession extends ChangeNotifier {
  WorkspaceSession({
    required this.chatName,
    Stream<ChatTurnEvent>? turns,
  }) {
    listenTurns(turns);
  }

  String chatName;

  final _turns = <ChatTurnEvent>[];
  final _seen = <int>{};
  List<ChatTurnEvent> projectedTurns = const [];
  String? turnFailure;

  StreamSubscription<ChatTurnEvent>? _turnSubscription;

  void updateChatName(String name) {
    if (chatName == name) {
      return;
    }
    chatName = name;
    _turns.clear();
    _seen.clear();
    projectedTurns = const [];
    notifyListeners();
  }

  void listenTurns(Stream<ChatTurnEvent>? turns) {
    unawaited(_turnSubscription?.cancel());
    turnFailure = null;
    _turnSubscription = turns?.listen(
      (turn) {
        if (!_seen.add(turn.sequence)) {
          return;
        }
        _turns.add(turn);
        _turns.sort((a, b) => a.sequence.compareTo(b.sequence));
        projectedTurns = List<ChatTurnEvent>.unmodifiable(_turns);
        notifyListeners();
      },
      onError: (Object error) {
        turnFailure = '$error';
        notifyListeners();
      },
    );
  }

  String? statusMessage(String? external) => external ?? turnFailure;

  @override
  void dispose() {
    unawaited(_turnSubscription?.cancel());
    super.dispose();
  }
}
